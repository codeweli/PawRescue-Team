using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PawRescue.Mobile.Models;
using PawRescue.Mobile.Services;

namespace PawRescue.Mobile.ViewModels;

public partial class ReportAnimalViewModel : BaseViewModel
{
    private const int MaxPhotos = 5;

    private readonly IReportApi _reports;
    private readonly IMediaService _media;
    private readonly ILocationService _location;

    public ReportAnimalViewModel(IReportApi reports, IMediaService media, ILocationService location, IDialogService dialogs)
        : base(dialogs)
    {
        _reports = reports;
        _media = media;
        _location = location;
        Title = "Report an Animal";
    }

    public ObservableCollection<PhotoItem> Photos { get; } = new();

    public AnimalType[] AnimalTypes { get; } = Enum.GetValues<AnimalType>();
    public AnimalCondition[] Conditions { get; } = Enum.GetValues<AnimalCondition>();
    public SeverityLevel[] Severities { get; } = Enum.GetValues<SeverityLevel>();

    [ObservableProperty] private string reportTitle = string.Empty;
    [ObservableProperty] private string description = string.Empty;
    [ObservableProperty] private AnimalType selectedAnimalType = AnimalType.Dog;
    [ObservableProperty] private AnimalCondition selectedCondition = AnimalCondition.Injured;
    [ObservableProperty] private SeverityLevel selectedSeverity = SeverityLevel.Medium;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLocation))]
    private double? latitude;

    [ObservableProperty] private double? longitude;
    [ObservableProperty] private string? address;
    [ObservableProperty] private string locationStatus = "Tap to detect your location";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPhotos))]
    [NotifyPropertyChangedFor(nameof(CanAddMorePhotos))]
    private int photoCount;

    public bool HasLocation => Latitude is not null;
    public bool HasPhotos => PhotoCount > 0;
    public bool CanAddMorePhotos => PhotoCount < MaxPhotos;

    public override async Task OnAppearingAsync()
    {
        if (!HasLocation) await DetectLocationAsync();
    }

    [RelayCommand]
    private async Task TakePhotoAsync() => AddPhoto(await _media.TakePhotoAsync());

    [RelayCommand]
    private async Task PickPhotoAsync() => AddPhoto(await _media.PickPhotoAsync());

    [RelayCommand]
    private void RemovePhoto(PhotoItem photo)
    {
        Photos.Remove(photo);
        PhotoCount = Photos.Count;
    }

    private void AddPhoto(PhotoItem? photo)
    {
        if (photo is null || Photos.Count >= MaxPhotos) return;
        Photos.Add(photo);
        PhotoCount = Photos.Count;
    }

    [RelayCommand]
    private async Task DetectLocationAsync()
    {
        LocationStatus = "Detecting location…";
        var point = await _location.GetCurrentAsync();
        if (point is null)
        {
            LocationStatus = "Couldn't get GPS. Turn on location and tap to retry.";
            return;
        }
        if (!Philippines.Contains(point))
        {
            LocationStatus = "Your location is outside the Philippines. Set a Philippine location and tap to retry.";
            return;
        }

        Latitude = point.Latitude;
        Longitude = point.Longitude;
        Address = await _location.GetAddressAsync(point.Latitude, point.Longitude);
        LocationStatus = Address ?? $"{point.Latitude:0.00000}, {point.Longitude:0.00000}";
    }

    [RelayCommand]
    private Task SubmitAsync() => RunAsync(async () =>
    {
        var error = Validate();
        if (error is not null) { await Dialogs.AlertAsync("Almost there", error); return; }

        var created = await _reports.CreateAsync(ReportTitle.Trim(), Description, SelectedAnimalType, SelectedCondition,
                                                 SelectedSeverity, Latitude!.Value, Longitude!.Value, Address, Photos);

        await Dialogs.AlertAsync("Report sent 🐾", "Thank you! Nearby volunteers have been notified.");
        Reset();
        // "../reportdetails" replaces this form with the new report, so Back returns to where the user started.
        await Shell.Current.GoToAsync("../reportdetails", new Dictionary<string, object> { ["id"] = created.Id });
    });

    private string? Validate()
    {
        if (Photos.Count == 0) return "Please add at least one photo of the animal.";
        if (string.IsNullOrWhiteSpace(ReportTitle)) return "Please add a short title (e.g. \"Injured puppy near market\").";
        if (Latitude is null || Longitude is null) return "We need the animal's location. Tap the location card to detect it.";
        return null;
    }

    private void Reset()
    {
        Photos.Clear();
        PhotoCount = 0;
        ReportTitle = Description = string.Empty;
        SelectedSeverity = SeverityLevel.Medium;
        SelectedCondition = AnimalCondition.Injured;
        SelectedAnimalType = AnimalType.Dog;
    }
}
