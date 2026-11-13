using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PawRescue.Mobile.Models;
using PawRescue.Mobile.Services;

namespace PawRescue.Mobile.ViewModels;

public partial class MapViewModel : BaseViewModel
{
    private readonly IReportApi _reports;
    private readonly ILocationService _location;
    private readonly IDirectionsService _directions;
    private List<ReportSummaryDto> _loaded = new();
    private CancellationTokenSource? _debounce;

    public MapViewModel(IReportApi reports, ILocationService location, IDirectionsService directions, IDialogService dialogs) : base(dialogs)
    {
        _reports = reports;
        _location = location;
        _directions = directions;
        Title = "Rescue Map";
    }

    /// <summary>Raised so the page can move the map camera (a pure View concern).</summary>
    public event Action<GeoPoint, double>? CenterRequested;

    public ObservableCollection<MapPinItem> Pins { get; } = new();

    public string[] SeverityFilters { get; } = new[] { "All", "Medium+", "High+", "Critical" };
    public string[] StatusFilters { get; } = new[] { "Open", "Reported", "In Progress", "Rescued", "All" };

    [ObservableProperty] private string selectedSeverityFilter = "All";
    [ObservableProperty] private string selectedStatusFilter = "Open";
    [ObservableProperty] private string searchText = string.Empty;
    [ObservableProperty] private string resultText = string.Empty;

    /// <summary>The report shown in the bottom preview card (null = card hidden).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private MapPinItem? selectedPin;

    public bool HasSelection => SelectedPin is not null;

    /// <summary>The phone's position (blue dot on the map). Null when unknown or outside the Philippines.</summary>
    [ObservableProperty] private GeoPoint? myLocation;

    /// <summary>The road route from MyLocation to the selected report (red line on the map).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRoute))]
    private RouteResult? route;

    public bool HasRoute => Route is not null;

    [ObservableProperty] private string routeText = string.Empty;
    [ObservableProperty] private bool isRouting;

    // A different report was tapped → the old route no longer applies.
    partial void OnSelectedPinChanged(MapPinItem? oldValue, MapPinItem? newValue)
    {
        if (oldValue?.ReportId != newValue?.ReportId) ClearRoute();
    }

    private void ClearRoute()
    {
        Route = null;
        RouteText = string.Empty;
    }

    /// <summary>"Directions" on the preview card: draw the road route from you to the animal inside the app.</summary>
    [RelayCommand]
    private async Task ShowRouteAsync()
    {
        if (SelectedPin is null || IsRouting) return;
        try
        {
            IsRouting = true;
            RouteText = "Finding the best route…";

            var me = await _location.GetCurrentInPhilippinesAsync();
            if (me is null)
            {
                RouteText = string.Empty;
                await Dialogs.AlertAsync("Where are you?",
                    "Turn on location so we can route you from your position. " +
                    "(On the emulator: ⋯ → Location → choose a place in the Philippines.)");
                return;
            }
            MyLocation = me;

            var target = new GeoPoint(SelectedPin.Location.Latitude, SelectedPin.Location.Longitude);
            var result = await _directions.GetRouteAsync(me, target);
            if (result is null)
            {
                RouteText = "Couldn't get a road route. Use \"Navigate\" to open Google Maps.";
                return;
            }

            Route = result;
            RouteText = result.Summary;

            // Zoom so both you and the animal are visible.
            var mid = new GeoPoint((me.Latitude + target.Latitude) / 2, (me.Longitude + target.Longitude) / 2);
            CenterRequested?.Invoke(mid, Math.Max(1, result.DistanceKm * 0.65));
        }
        finally
        {
            IsRouting = false;
        }
    }

    /// <summary>"Navigate": turn-by-turn directions in Google Maps (app or browser) from your exact position.</summary>
    [RelayCommand]
    private async Task NavigateAsync()
    {
        if (SelectedPin is null) return;
        var me = MyLocation ?? await _location.GetCurrentInPhilippinesAsync();
        try
        {
            await _directions.OpenNavigationAsync(me, new GeoPoint(SelectedPin.Location.Latitude, SelectedPin.Location.Longitude));
        }
        catch
        {
            await Dialogs.AlertAsync("Can't open maps", "No maps app or browser could open the directions.");
        }
    }

    /// <summary>Legend shown in the top-left corner of the map.</summary>
    public LegendItem[] Legend { get; } =
    {
        new(Needs.MapLabel(NeedCategory.Medical), Needs.ColorOf(NeedCategory.Medical)),
        new(Needs.MapLabel(NeedCategory.Food), Needs.ColorOf(NeedCategory.Food)),
        new(Needs.MapLabel(NeedCategory.Shelter), Needs.ColorOf(NeedCategory.Shelter)),
        new(Needs.MapLabel(NeedCategory.Provided), Needs.ColorOf(NeedCategory.Provided))
    };

    private GeoPoint _center = Philippines.DefaultCenter; // San Pablo City until GPS answers
    private double _radiusKm = 5;
    private bool _started;

    public override async Task OnAppearingAsync()
    {
        if (_started) return;
        _started = true;

        // GPS outside the Philippines (e.g. the emulator's default) is ignored → start in San Pablo City.
        var here = await _location.GetCurrentInPhilippinesAsync();
        MyLocation = here;
        _center = here ?? Philippines.DefaultCenter;
        _radiusKm = here is null ? 3 : 5;
        CenterRequested?.Invoke(_center, _radiusKm);
        await LoadAsync();
    }

    /// <summary>Called by the page when the user pans/zooms; debounced so we don't spam the API.</summary>
    public void OnVisibleRegionChanged(double lat, double lng, double radiusKm)
    {
        _debounce?.Cancel();
        _debounce = new CancellationTokenSource();
        var token = _debounce.Token;

        Task.Delay(600, token).ContinueWith(async t =>
        {
            if (t.IsCanceled) return;
            var movedFar = Math.Abs(lat - _center.Latitude) > radiusKm / 222 || Math.Abs(lng - _center.Longitude) > radiusKm / 222;
            var zoomChanged = Math.Abs(radiusKm - _radiusKm) / Math.Max(_radiusKm, 0.1) > 0.3;
            _center = new GeoPoint(lat, lng);
            _radiusKm = Math.Clamp(radiusKm, 0.5, 100);

            if (movedFar || zoomChanged)
                await MainThread.InvokeOnMainThreadAsync(LoadAsync);
        }, TaskScheduler.Default);
    }

    [RelayCommand]
    private Task LoadAsync() => RunAsync(async () =>
    {
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var result = await _reports.SearchAsync(SearchText, StatusFilter(), null);
            _loaded = result.Items.Where(MatchesSeverity).ToList();
        }
        else
        {
            var status = StatusFilter();
            var openOnly = SelectedStatusFilter == "Open";
            var all = await _reports.GetNearbyAsync(_center.Latitude, _center.Longitude, _radiusKm,
                                                    MinSeverity(), status, openOnly);
            _loaded = all;
        }
        RebuildPins();
    }, showBusy: false);

    [RelayCommand]
    private async Task CenterOnMeAsync()
    {
        var here = await _location.GetCurrentAsync();
        if (here is null) { await Dialogs.AlertAsync("Location off", "Turn on location services to center the map on you."); return; }
        if (!Philippines.Contains(here))
        {
            await Dialogs.AlertAsync("Outside the Philippines",
                "Your location appears to be outside the Philippines, so the map stays on the Philippines. " +
                "(On the emulator: ⋯ → Location → choose a place in the Philippines.)");
            return;
        }
        _center = here;
        MyLocation = here;
        CenterRequested?.Invoke(here, 2);
    }

    partial void OnSelectedSeverityFilterChanged(string value) => LoadCommand.Execute(null);
    partial void OnSelectedStatusFilterChanged(string value) => LoadCommand.Execute(null);

    /// <summary>
    /// Search bar: first try it as a PLACE in the Philippines ("Muñoz", "SM Megamall") and move the map there.
    /// If no place matches, search the REPORTS instead (name, notes, address).
    /// </summary>
    [RelayCommand]
    private async Task SearchAsync()
    {
        var text = SearchText?.Trim() ?? string.Empty;
        if (text.Length == 0) { await LoadAsync(); return; }

        ResultText = "Searching…";
        var place = await _location.SearchPlaceAsync(text);
        if (place is not null)
        {
            SearchText = string.Empty;          // back to "reports near the map center" mode
            _center = place.Point;
            _radiusKm = 5;
            CenterRequested?.Invoke(place.Point, _radiusKm);
            await LoadAsync();
            ResultText = $"{_loaded.Count} report(s) near {place.Name}";
            return;
        }

        await LoadAsync();                      // keyword search on reports
        if (_loaded.Count == 0) ResultText = $"No place or report found for \"{text}\"";
    }

    /// <summary>Called when a pin is tapped: single report → preview card, cluster → zoom in.</summary>
    public async Task SelectPinAsync(MapPinItem pin)
    {
        if (pin.IsCluster || pin.ReportId is null)
        {
            SelectedPin = null;
            await OpenPinAsync(pin);
        }
        else
        {
            SelectedPin = pin;
        }
    }

    [RelayCommand]
    private void ClosePreview()
    {
        SelectedPin = null;
        ClearRoute();
    }

    [RelayCommand]
    private async Task ViewDetailsAsync()
    {
        if (SelectedPin?.ReportId is { } id)
            await Shell.Current.GoToAsync("reportdetails", new Dictionary<string, object> { ["id"] = id });
    }

    /// <summary>Called when the user taps a pin's info window.</summary>
    public async Task OpenPinAsync(MapPinItem pin)
    {
        if (pin.ReportId is { } id)
        {
            await Shell.Current.GoToAsync("reportdetails", new Dictionary<string, object> { ["id"] = id });
        }
        else
        {
            // Cluster: zoom in on it.
            CenterRequested?.Invoke(new GeoPoint(pin.Location.Latitude, pin.Location.Longitude), Math.Max(_radiusKm / 4, 0.5));
        }
    }

    private void RebuildPins()
    {
        var newPins = MarkerClusterer.Cluster(_loaded, _radiusKm);

        // Keep the preview card open if its report is still on the map after reloading.
        var selectedId = SelectedPin?.ReportId;
        SelectedPin = selectedId is null ? null : newPins.FirstOrDefault(p => p.ReportId == selectedId);

        Pins.Clear();
        foreach (var pin in newPins) Pins.Add(pin);
        ResultText = _loaded.Count == 0 ? "No reports in this area" : $"{_loaded.Count} report(s) in view";
    }

    // TODO(MEMBER 3): Turn the picker text into a severity:
    //   "Medium+" → SeverityLevel.Medium, "High+" → High, "Critical" → Critical, anything else → null.
    private SeverityLevel? MinSeverity() => null;

    private bool MatchesSeverity(ReportSummaryDto r) => MinSeverity() is not { } min || r.Severity >= min;

    // TODO(MEMBER 3): Turn the picker text into a status:
    //   "Reported" → ReportStatus.Reported, "In Progress" → InProgress, "Rescued" → Rescued, else → null.
    //   ("Open" and "All" stay null — LoadAsync sends openOnly = true for "Open".)
    private ReportStatus? StatusFilter() => null;
}

public record LegendItem(string Label, Color Color);
