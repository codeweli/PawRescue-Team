using System.ComponentModel;
using Microsoft.Maui.Controls.Maps;
using Microsoft.Maui.Maps;
using PawRescue.Mobile.Models;
using PawRescue.Mobile.Services;
using PawRescue.Mobile.ViewModels;

namespace PawRescue.Mobile.Views;

/// <summary>
/// Camera movement and pin taps are View concerns (MAUI Maps can't bind the camera),
/// so this code-behind only forwards them to MapViewModel.
/// </summary>
public partial class MapPage : BasePage
{
    private readonly MapViewModel _vm;

    public MapPage(MapViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;

        _vm.CenterRequested += MoveCamera;
        RescueMap.PropertyChanged += OnMapPropertyChanged;
    }

    private void MoveCamera(GeoPoint center, double radiusKm)
        => MainThread.BeginInvokeOnMainThread(() =>
            RescueMap.MoveToRegion(MapSpan.FromCenterAndRadius(
                new Location(center.Latitude, center.Longitude), Distance.FromKilometers(radiusKm))));

    private void OnMapPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != "VisibleRegion" || RescueMap.VisibleRegion is not { } region) return;
        _vm.OnVisibleRegionChanged(region.Center.Latitude, region.Center.Longitude, region.Radius.Kilometers);
    }

    /// <summary>Tapping a pin shows the bottom preview card instead of Google's small info bubble.</summary>
    private async void OnMarkerClicked(object? sender, PinClickedEventArgs e)
    {
        e.HideInfoWindow = true;
        if (sender is Pin { BindingContext: MapPinItem item })
            await _vm.SelectPinAsync(item);
    }

    private async void OnInfoWindowClicked(object? sender, PinClickedEventArgs e)
    {
        e.HideInfoWindow = true;
        if (sender is Pin { BindingContext: MapPinItem item })
            await _vm.OpenPinAsync(item);
    }
}
