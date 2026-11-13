using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using NetTopologySuite.Geometries;
using Mapsui.Projections;
using Mapsui.Styles.Thematics;
using Mapsui.Tiling;
using PawRescue.Mobile.Models;
using PawRescue.Mobile.Services;
using PawRescue.Mobile.ViewModels;
// Mapsui and MAUI both have Color/Brush/Pen classes, so give Mapsui's short aliases.
using MBrush = Mapsui.Styles.Brush;
using MColor = Mapsui.Styles.Color;
using MPen = Mapsui.Styles.Pen;
using MSymbolStyle = Mapsui.Styles.SymbolStyle;
using MVectorStyle = Mapsui.Styles.VectorStyle;

namespace PawRescue.Mobile.Views;

/// <summary>
/// Map screen using OpenStreetMap through the free Mapsui library (no API key, no billing).
///
/// The ViewModel (MapViewModel) stays the same: it fills <c>Pins</c> and raises <c>CenterRequested</c>.
/// This code-behind only does "view" work:
///   • draws each MapPinItem as a colored dot on a layer,
///   • moves the camera,
///   • tells the ViewModel when the user pans/zooms or taps a dot.
///
/// Coordinates: GPS uses latitude/longitude, but web maps use "Spherical Mercator" (meters).
/// SphericalMercator.FromLonLat / ToLonLat converts between the two.
/// </summary>
public partial class MapPage : BasePage
{
    private readonly MapViewModel _vm;
    private readonly Mapsui.Map _map = new();
    private readonly WritableLayer _pinLayer = new() { Name = "Reports" };
    private readonly WritableLayer _routeLayer = new() { Name = "Route" };
    private readonly WritableLayer _meLayer = new() { Name = "Me" };

    // A camera move requested before the map has a size is remembered and applied later.
    private (GeoPoint Center, double RadiusKm)? _pendingMove;
    private bool _redrawQueued;

    public MapPage(MapViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;

        // 1. Background tiles from OpenStreetMap (the user agent identifies our app to the OSM tile servers).
        _map.Layers.Add(OpenStreetMap.CreateTileLayer("PawRescue/0.1 (student capstone app)"));

        // 2a. Route line from you to the selected animal (drawn under the dots)
        _routeLayer.Style = new MVectorStyle { Line = new MPen(new MColor(200, 36, 59), 6) };
        _map.Layers.Add(_routeLayer);

        // 2b. Our report dots, colored by need: red = medical, orange = food, yellow = shelter, green = helped.
        _pinLayer.Style = new ThemeStyle(feature =>
        {
            if (feature["pin"] is not MapPinItem pin) return null;
            return new MSymbolStyle
            {
                SymbolScale = pin.IsCluster ? 0.9 : 0.6,
                Fill = new MBrush(ToMapsui(pin.NeedColor)),
                Outline = new MPen(new MColor(255, 255, 255), 3)
            };
        });
        _map.Layers.Add(_pinLayer);

        // 2c. "You are here" — blue dot with a white ring, drawn on top
        _meLayer.Style = new MSymbolStyle
        {
            SymbolScale = 0.5,
            Fill = new MBrush(new MColor(47, 128, 237)),
            Outline = new MPen(new MColor(255, 255, 255), 4)
        };
        _map.Layers.Add(_meLayer);

        // 3. Keep the map on the Philippines: the center can't leave the country's box
        //    and you can't zoom out further than roughly the whole country.
        var (minX, minY) = SphericalMercator.FromLonLat(Philippines.MinLongitude, Philippines.MinLatitude);
        var (maxX, maxY) = SphericalMercator.FromLonLat(Philippines.MaxLongitude, Philippines.MaxLatitude);
        _map.Navigator.OverridePanBounds = new MRect(minX, minY, maxX, maxY);
        _map.Navigator.OverrideZoomBounds = new MMinMax(0.3, 2500);   // meters per pixel: street level … whole country

        RescueMap.Map = _map;

        // 4. Wire up ViewModel ⇄ map
        _vm.CenterRequested += MoveCamera;
        _vm.Pins.CollectionChanged += (_, _) => QueueRedraw();
        _map.Tapped += (_, e) => OnMapTapped(e.GetMapInfo(new ILayer[] { _pinLayer })?.Feature);
        _map.Navigator.ViewportChanged += (_, _) => OnViewportChanged();
        RescueMap.SizeChanged += (_, _) => ApplyPendingMove();
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MapViewModel.MyLocation)) Dispatcher.Dispatch(RedrawMe);
            if (e.PropertyName == nameof(MapViewModel.Route)) Dispatcher.Dispatch(RedrawRoute);
        };

        RedrawPins();
        RedrawMe();
        MoveCamera(_vm.CurrentCenter, _vm.CurrentRadiusKm);   // applied once the map has a size
    }

    /// <summary>Blue dot at the phone's position.</summary>
    private void RedrawMe()
    {
        _meLayer.Clear();
        if (_vm.MyLocation is { } me)
        {
            var (x, y) = SphericalMercator.FromLonLat(me.Longitude, me.Latitude);
            _meLayer.Add(new PointFeature(x, y));
        }
        _meLayer.DataHasChanged();
    }

    /// <summary>Red road line from you to the selected animal (from the free OSRM routing service).</summary>
    private void RedrawRoute()
    {
        _routeLayer.Clear();
        if (_vm.Route is { Points.Count: >= 2 } route)
        {
            var coordinates = route.Points.Select(p =>
            {
                var (x, y) = SphericalMercator.FromLonLat(p.Longitude, p.Latitude);
                return new Coordinate(x, y);
            }).ToArray();
            _routeLayer.Add(new GeometryFeature(new LineString(coordinates)));
        }
        _routeLayer.DataHasChanged();
    }

    /// <summary>
    /// The ViewModel clears the pins and adds them one by one. Instead of redrawing the layer
    /// for every single change (which made the map flicker), redraw once after all changes.
    /// </summary>
    private void QueueRedraw()
    {
        if (_redrawQueued) return;
        _redrawQueued = true;
        Dispatcher.Dispatch(() =>
        {
            _redrawQueued = false;
            RedrawPins();
        });
    }

    /// <summary>Re-creates one dot per MapPinItem.</summary>
    private void RedrawPins()
    {
        _pinLayer.Clear();
        foreach (var pin in _vm.Pins)
        {
            var (x, y) = SphericalMercator.FromLonLat(pin.Location.Longitude, pin.Location.Latitude);
            var feature = new PointFeature(x, y);
            feature["pin"] = pin;           // keep the item so a tap can find it again
            _pinLayer.Add(feature);
        }
        _pinLayer.DataHasChanged();
    }

    /// <summary>Tap on a dot → preview card (or zoom into a cluster). Tap on empty map → close the card.</summary>
    private async void OnMapTapped(IFeature? feature)
    {
        if (feature?["pin"] is MapPinItem pin)
            await _vm.SelectPinAsync(pin);
        else
            _vm.ClosePreviewCommand.Execute(null);
    }

    /// <summary>The user panned or zoomed: tell the ViewModel the new center and visible radius.</summary>
    private void OnViewportChanged()
    {
        var vp = _map.Navigator.Viewport;
        if (vp.Width <= 0 || vp.Height <= 0) return;

        var (lon, lat) = SphericalMercator.ToLonLat(vp.CenterX, vp.CenterY);
        // Resolution = map meters per screen pixel. Mercator stretches distances by 1/cos(latitude).
        var halfScreenMeters = vp.Resolution * Math.Max(vp.Width, vp.Height) / 2;
        var radiusKm = halfScreenMeters * Math.Cos(lat * Math.PI / 180) / 1000;
        _vm.OnVisibleRegionChanged(lat, lon, radiusKm);
    }

    /// <summary>Center the map on a point so that about radiusKm is visible on each side.</summary>
    private void MoveCamera(GeoPoint center, double radiusKm)
    {
        if (RescueMap.Width <= 0)
        {
            _pendingMove = (center, radiusKm);   // not laid out yet: do it on SizeChanged
            return;
        }

        MainThread.BeginInvokeOnMainThread(() =>
        {
            var (x, y) = SphericalMercator.FromLonLat(center.Longitude, center.Latitude);
            var halfWidthPx = Math.Max(RescueMap.Width, 200) / 2;
            var meters = radiusKm * 1000 / Math.Cos(center.Latitude * Math.PI / 180);
            _map.Navigator.CenterOnAndZoomTo(new MPoint(x, y), meters / halfWidthPx);
        });
    }

    private void ApplyPendingMove()
    {
        if (_pendingMove is not { } move || RescueMap.Width <= 0) return;
        _pendingMove = null;
        MoveCamera(move.Center, move.RadiusKm);
    }

    private static MColor ToMapsui(Color c) =>
        new((int)(c.Red * 255), (int)(c.Green * 255), (int)(c.Blue * 255));
}
