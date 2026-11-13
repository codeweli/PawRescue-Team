using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PawRescue.Mobile.Models;
using PawRescue.Mobile.Services;

namespace PawRescue.Mobile.ViewModels;

/// <summary>One step of the rescue progress tracker.</summary>
public record TimelineStep(string Label, bool IsDone, bool IsCurrent);

/// <summary>Receives ?id=... from Shell navigation through IQueryAttributable.</summary>
public partial class ReportDetailsViewModel : BaseViewModel, IQueryAttributable
{
    private static readonly (ReportStatus Status, string Label)[] Steps =
    {
        (ReportStatus.Reported, "Reported"),
        (ReportStatus.UnderReview, "Under review"),
        (ReportStatus.RescueAssigned, "Rescuer assigned"),
        (ReportStatus.InProgress, "Rescue in progress"),
        (ReportStatus.Rescued, "Rescued"),
        (ReportStatus.Closed, "Closed")
    };

    private readonly IReportApi _reports;
    private readonly IRescueApi _rescues;
    private readonly TokenStore _tokens;
    private Guid _id;

    private readonly ILocationService _location;
    private readonly IDirectionsService _directions;
    private readonly MapViewModel _map;

    public ReportDetailsViewModel(IReportApi reports, IRescueApi rescues, TokenStore tokens,
                                  ILocationService location, IDirectionsService directions, MapViewModel map,
                                  IDialogService dialogs) : base(dialogs)
    {
        _map = map;
        _location = location;
        _directions = directions;
        _reports = reports;
        _rescues = rescues;
        _tokens = tokens;
        Title = "Animal Details";
    }

    public ObservableCollection<ImageDto> Images { get; } = new();
    public ObservableCollection<TimelineStep> Timeline { get; } = new();

    [ObservableProperty] private ReportDto? report;
    [ObservableProperty] private bool canAccept;
    [ObservableProperty] private bool canUpdateStatus;
    [ObservableProperty] private bool canDelete;
    [ObservableProperty] private bool canEdit;
    [ObservableProperty] private bool canDrop;
    [ObservableProperty] private string nextActionText = string.Empty;
    [ObservableProperty] private string reportedAgo = string.Empty;

    // ----- Display helpers for the redesigned screen -----
    [ObservableProperty] private string subtitle = string.Empty;      // "Cat · reported 2 h ago"
    [ObservableProperty] private string needText = string.Empty;      // "Medical Treatment"
    [ObservableProperty] private Color needColor = Colors.Gray;
    [ObservableProperty] private string urgencyText = string.Empty;   // "High urgency"
    [ObservableProperty] private string statusText = string.Empty;    // "Rescue in progress"
    [ObservableProperty] private bool hasPhotos;
    [ObservableProperty] private bool hasRescuer;

    /// <summary>The friendly "Accept this rescue?" sheet (instead of the plain Android dialog).</summary>
    [ObservableProperty] private bool isAcceptSheetVisible;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("id", out var value) && value is Guid id)
        {
            _id = id;
            LoadCommand.Execute(null);
        }
    }

    [RelayCommand]
    private Task LoadAsync() => RunAsync(LoadCoreAsync);

    private async Task LoadCoreAsync()
    {
        var r = await _reports.GetAsync(_id);
        Report = r;

        Images.Clear();
        foreach (var img in r.Images) Images.Add(img);

        Timeline.Clear();
        var currentIndex = Array.FindIndex(Steps, s => s.Status == r.Status);
        for (var i = 0; i < Steps.Length; i++)
        {
            if (Steps[i].Status == ReportStatus.Closed && r.Status != ReportStatus.Closed) continue;
            Timeline.Add(new TimelineStep(Steps[i].Label, i <= currentIndex, i == currentIndex));
        }

        ReportedAgo = Ago(r.CreatedAt);
        Subtitle = $"{r.AnimalType} · reported {ReportedAgo}";
        var need = Needs.From(r.Condition, r.Status);
        NeedText = Needs.CardLabel(r.Condition, r.Status);
        NeedColor = Needs.ColorOf(need);
        UrgencyText = $"{r.Severity} urgency";
        StatusText = Steps.FirstOrDefault(s => s.Status == r.Status).Label ?? r.Status.ToString();
        HasPhotos = r.Images.Count > 0;
        HasRescuer = r.ActiveAssignment is not null;
        ComputePermissions(r);
    }

    private void ComputePermissions(ReportDto r)
    {
        var me = _tokens.CurrentUser?.Id;
        var isAssignedToMe = r.ActiveAssignment?.VolunteerId == me;
        var isOwner = r.ReporterId == me;

        CanAccept = _tokens.IsVolunteer && r.ActiveAssignment is null &&
                    r.Status is ReportStatus.Reported or ReportStatus.UnderReview;
        CanUpdateStatus = (isAssignedToMe || _tokens.IsAdmin) && NextStatus(r.Status) is not null;
        CanDelete = (isOwner && r.Status == ReportStatus.Reported) || _tokens.IsAdmin;
        CanDrop = isAssignedToMe && r.Status == ReportStatus.RescueAssigned;
        CanEdit = isOwner && r.Status is not (ReportStatus.Rescued or ReportStatus.Closed);
        NextActionText = NextStatus(r.Status) switch
        {
            ReportStatus.InProgress => "Start rescue",
            ReportStatus.Rescued => "Mark as rescued",
            ReportStatus.Closed => "Close case",
            ReportStatus.RescueAssigned => "Assign",
            _ => string.Empty
        };
    }

    private static ReportStatus? NextStatus(ReportStatus s) => s switch
    {
        ReportStatus.RescueAssigned => ReportStatus.InProgress,
        ReportStatus.InProgress => ReportStatus.Rescued,
        ReportStatus.Rescued => ReportStatus.Closed,
        _ => null
    };

    /// <summary>"Accept Rescue" button → shows the confirmation sheet.</summary>
    [RelayCommand]
    private void Accept() => IsAcceptSheetVisible = true;

    [RelayCommand]
    private void CancelAccept() => IsAcceptSheetVisible = false;

    /// <summary>"Yes, I'll help" on the sheet → accept the rescue.</summary>
    [RelayCommand]
    private Task ConfirmAcceptAsync() => RunAsync(async () =>
    {
        IsAcceptSheetVisible = false;
        await _rescues.AcceptAsync(_id);
        await LoadCoreAsync();
        await Dialogs.AlertAsync("Thank you! 🐾", $"You're now the rescuer for {Report?.Title}. The reporter has been notified.");
    });

    // ----- "Start rescue / Mark as rescued / Close case" sheet -----
    [ObservableProperty] private bool isStatusSheetVisible;
    [ObservableProperty] private string statusNote = string.Empty;
    [ObservableProperty] private string statusSheetHint = string.Empty;
    public ObservableCollection<string> QuickNotes { get; } = new();

    /// <summary>Main volunteer button → opens the update sheet with ready-made notes for the next step.</summary>
    [RelayCommand]
    private void AdvanceStatus()
    {
        if (Report is null || NextStatus(Report.Status) is not { } next) return;

        StatusNote = string.Empty;
        QuickNotes.Clear();
        var (hint, notes) = next switch
        {
            ReportStatus.InProgress => ("Tell the reporter you're starting the rescue.",
                                        new[] { "On my way 🚗", "Arrived at the location", "Bringing food and water" }),
            ReportStatus.Rescued => ("Great work! Let the reporter know the animal is safe.",
                                     new[] { "Brought to the vet 🏥", "Safe with a foster home 🏠", "Back with its owner" }),
            _ => ("Close this case when nothing more is needed.",
                  new[] { "Adopted 💛", "Released healthy", "Duplicate report" })
        };
        StatusSheetHint = hint;
        foreach (var n in notes) QuickNotes.Add(n);
        IsStatusSheetVisible = true;
    }

    [RelayCommand]
    private void PickQuickNote(string note) => StatusNote = note;

    [RelayCommand]
    private void CancelStatus() => IsStatusSheetVisible = false;

    [RelayCommand]
    private Task ConfirmStatusAsync() => RunAsync(async () =>
    {
        if (Report is null || NextStatus(Report.Status) is not { } next) return;
        IsStatusSheetVisible = false;
        await _rescues.UpdateStatusAsync(_id, next, string.IsNullOrWhiteSpace(StatusNote) ? null : StatusNote.Trim());
        await LoadCoreAsync();
    });

    /// <summary>Volunteer gives the case back (it becomes available for others).</summary>
    [RelayCommand]
    private Task DropRescueAsync() => RunAsync(async () =>
    {
        if (!await Dialogs.ConfirmAsync("Drop this rescue?", "The case will go back to the list for other volunteers.", "Drop")) return;
        await _rescues.UpdateStatusAsync(_id, ReportStatus.UnderReview, "Volunteer released the case");
        await LoadCoreAsync();
    });

    [RelayCommand]
    private Task DeleteAsync() => RunAsync(async () =>
    {
        if (!await Dialogs.ConfirmAsync("Delete report?", "This cannot be undone.", "Delete")) return;
        await _reports.DeleteAsync(_id);
        await Shell.Current.GoToAsync("..");
    });

    /// <summary>Share the report through Messenger, SMS, etc. so more people can help.</summary>
    [RelayCommand]
    private async Task ShareAsync()
    {
        if (Report is null) return;
        var mapLink = $"https://maps.google.com/?q={Report.Location.Latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}," +
                      $"{Report.Location.Longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        var text = $"🐾 {Report.Title} ({Report.AnimalType}) needs help: {Needs.CardLabel(Report.Condition, Report.Status)}.\n" +
                   $"📍 {Report.Location.Address ?? "See map"}\n{mapLink}\nReported on Paw Rescue.";
        await Share.Default.RequestAsync(new ShareTextRequest { Title = "Share this report", Text = text });
    }

    /// <summary>Tapping the location card: open the in-app Rescue Map zoomed on this exact report.</summary>
    [RelayCommand]
    private async Task ShowOnMapAsync()
    {
        if (Report is null) return;
        var point = new GeoPoint(Report.Location.Latitude, Report.Location.Longitude);
        var id = Report.Id;
        await Shell.Current.GoToAsync("//map");
        await _map.FocusReportAsync(id, point);
    }

    [RelayCommand]
    private async Task OpenDirectionsAsync()
    {
        if (Report is null) return;
        // Start from the phone's real position (if it's in the Philippines) and go to the report's exact coordinates.
        var from = await _location.GetCurrentInPhilippinesAsync();
        var to = new GeoPoint(Report.Location.Latitude, Report.Location.Longitude);
        try
        {
            await _directions.OpenNavigationAsync(from, to);
        }
        catch
        {
            await Dialogs.AlertAsync("Can't open maps", "No maps app or browser could open the directions.");
        }
    }

    private static string Ago(DateTime utc)
    {
        var span = DateTime.UtcNow - utc.ToUniversalTime();
        if (span.TotalMinutes < 1) return "just now";
        if (span.TotalHours < 1) return $"{(int)span.TotalMinutes} min ago";
        if (span.TotalDays < 1) return $"{(int)span.TotalHours} h ago";
        return $"{(int)span.TotalDays} d ago";
    }
}
