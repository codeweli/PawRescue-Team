using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PawRescue.Mobile.Models;
using PawRescue.Mobile.Services;

namespace PawRescue.Mobile.ViewModels;

public partial class HomeViewModel : BaseViewModel
{
    private readonly IReportApi _reports;
    private readonly IUserApi _users;
    private readonly INotificationApi _notifications;
    private readonly ILocationService _location;
    private readonly IAppSettings _settings;
    private readonly RescueCasesViewModel _rescueCases;
    private readonly TokenStore _tokens;

    public HomeViewModel(IReportApi reports, IUserApi users, INotificationApi notifications,
                         ILocationService location, IAppSettings settings, RescueCasesViewModel rescueCases,
                         TokenStore tokens, IDialogService dialogs) : base(dialogs)
    {
        _reports = reports;
        _users = users;
        _notifications = notifications;
        _location = location;
        _settings = settings;
        _rescueCases = rescueCases;
        _tokens = tokens;
    }

    public ObservableCollection<ReportSummaryDto> NearbyReports { get; } = new();

    [ObservableProperty] private string greeting = string.Empty;
    [ObservableProperty] private bool isVolunteer;
    [ObservableProperty] private bool isAdmin;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnread))]
    private int unreadCount;

    public bool HasUnread => UnreadCount > 0;

    // Stats card: "128 Animals Reported · 94 Animals Helped · 21 Active Cases"
    [ObservableProperty] private string animalsReported = "–";
    [ObservableProperty] private string animalsHelped = "–";
    [ObservableProperty] private string activeCases = "–";

    [ObservableProperty] private bool isRefreshing;
    [ObservableProperty] private string searchText = string.Empty;
    [ObservableProperty] private string nearbyTitle = "Needs help nearby";
    [ObservableProperty] private ReportSummaryDto? selectedReport;

    public override async Task OnAppearingAsync()
    {
        var firstName = _tokens.CurrentUser?.FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "friend";
        var timeOfDay = DateTime.Now.Hour switch { < 12 => "Good morning", < 18 => "Good afternoon", _ => "Good evening" };
        Greeting = $"{timeOfDay}, {firstName}";
        IsVolunteer = _tokens.IsVolunteer;
        IsAdmin = _tokens.IsAdmin;
        await LoadAsync();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        await RunAsync(LoadCoreAsync, showBusy: !IsRefreshing);
        IsRefreshing = false;
    }

    private async Task LoadCoreAsync()
    {
        var here = await _location.GetCurrentAsync();
        if (here is null)
        {
            NearbyTitle = "Latest reports";
            var latest = await _reports.SearchAsync(null);
            Replace(latest.Items.Where(r => r.IsOpen).Take(10));
        }
        else
        {
            NearbyTitle = "Needs help nearby";
            Replace((await _reports.GetNearbyAsync(here.Latitude, here.Longitude, _settings.HomeRadiusKm)).Take(10));

            // Lets the server send "new nearby report" alerts to volunteers (can be turned off in settings).
            if (IsVolunteer && _settings.NearbyAlertsEnabled)
                await _users.UpdateLocationAsync(here.Latitude, here.Longitude);
        }

        var stats = await _reports.GetCommunityStatsAsync();
        AnimalsReported = stats.AnimalsReported.ToString("N0");
        AnimalsHelped = stats.AnimalsHelped.ToString("N0");
        ActiveCases = stats.ActiveCases.ToString("N0");

        UnreadCount = await _notifications.GetUnreadCountAsync();
    }

    private void Replace(IEnumerable<ReportSummaryDto> items)
    {
        NearbyReports.Clear();
        foreach (var r in items) NearbyReports.Add(r);
    }

    // Selecting a card opens its details, then clears the selection so it can be tapped again.
    partial void OnSelectedReportChanged(ReportSummaryDto? value)
    {
        if (value is null) return;
        SelectedReport = null;
        _ = Shell.Current.GoToAsync("reportdetails", new Dictionary<string, object> { ["id"] = value.Id });
    }

    /// <summary>The Home search bar searches on the Rescue Cases (Reports) tab.</summary>
    [RelayCommand]
    private async Task SearchAsync()
    {
        _rescueCases.PrepareSearch(SearchText);
        SearchText = string.Empty;
        await Shell.Current.GoToAsync("//reports");
    }

    [RelayCommand] private Task ReportAnimalAsync() => Shell.Current.GoToAsync("reportanimal");
    [RelayCommand] private Task OpenMapAsync() => Shell.Current.GoToAsync("//map");
    [RelayCommand] private Task OpenCasesAsync() => Shell.Current.GoToAsync("//reports");
    [RelayCommand] private Task OpenNotificationsAsync() => Shell.Current.GoToAsync("//notifications");
    [RelayCommand] private Task OpenVolunteerDashboardAsync() => Shell.Current.GoToAsync("volunteer");
    [RelayCommand] private Task OpenAdminDashboardAsync() => Shell.Current.GoToAsync("admin");
}
