using PawRescue.Mobile.Models;

namespace PawRescue.Mobile.Services;

public record GeoPoint(double Latitude, double Longitude);

/// <summary>A place found by the map search bar, e.g. "Science City of Muñoz, Nueva Ecija".</summary>
public record PlaceResult(GeoPoint Point, string Name);

/// <summary>
/// Paw Rescue is for the Philippines: the map is kept inside this box and
/// GPS positions outside it (e.g. the emulator's default California location) are ignored.
/// </summary>
public static class Philippines
{
    public const double MinLatitude = 4.5, MaxLatitude = 21.3;
    public const double MinLongitude = 116.8, MaxLongitude = 126.7;

    /// <summary>Used when GPS is off or outside the country: San Pablo City, Laguna (where the sample reports are).</summary>
    public static readonly GeoPoint DefaultCenter = new(14.0698, 121.3265);

    public static bool Contains(GeoPoint? p) =>
        p is not null &&
        p.Latitude is >= MinLatitude and <= MaxLatitude &&
        p.Longitude is >= MinLongitude and <= MaxLongitude;
}

public interface ILocationService
{
    /// <summary>Returns the current GPS position, or null if permission was denied / GPS is off.</summary>
    Task<GeoPoint?> GetCurrentAsync();

    /// <summary>Like GetCurrentAsync, but returns null when the position is outside the Philippines.</summary>
    Task<GeoPoint?> GetCurrentInPhilippinesAsync();

    Task<string?> GetAddressAsync(double latitude, double longitude);

    /// <summary>Finds a place in the Philippines by name ("Cabanatuan market", "SM North EDSA").</summary>
    Task<PlaceResult?> SearchPlaceAsync(string query);
}

/// <summary>
/// GPS through MAUI Geolocation, and place names through OpenStreetMap's free Nominatim service
/// (no API key; their rules ask for an app name in the User-Agent and at most ~1 request per second,
/// which is fine because we only search when the user presses Enter).
/// </summary>
public class LocationService : ILocationService
{
    private static readonly HttpClient Nominatim = CreateNominatimClient();
    private GeoPoint? _lastKnown;

    private static HttpClient CreateNominatimClient()
    {
        var http = new HttpClient { BaseAddress = new Uri("https://nominatim.openstreetmap.org/"), Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("PawRescue/0.1 (student capstone app)");
        http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en");
        return http;
    }

    public async Task<GeoPoint?> GetCurrentAsync()
    {
        try
        {
            var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
            if (status != PermissionStatus.Granted)
                status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
            if (status != PermissionStatus.Granted) return _lastKnown;

            var location = await Geolocation.Default.GetLocationAsync(
                new GeolocationRequest(GeolocationAccuracy.High, TimeSpan.FromSeconds(10)))
                ?? await Geolocation.Default.GetLastKnownLocationAsync();

            if (location is not null) _lastKnown = new GeoPoint(location.Latitude, location.Longitude);
            return _lastKnown;
        }
        catch (Exception)
        {
            // FeatureNotEnabledException (GPS off), FeatureNotSupportedException, etc.
            return _lastKnown;
        }
    }

    public async Task<GeoPoint?> GetCurrentInPhilippinesAsync()
    {
        var here = await GetCurrentAsync();
        return Philippines.Contains(here) ? here : null;
    }

    public async Task<string?> GetAddressAsync(double latitude, double longitude)
    {
        // 1) OpenStreetMap (works on the emulator, no Google services needed)
        try
        {
            var url = $"reverse?format=jsonv2&zoom=17&lat={F(latitude)}&lon={F(longitude)}";
            using var doc = System.Text.Json.JsonDocument.Parse(await Nominatim.GetStringAsync(url));
            if (doc.RootElement.TryGetProperty("address", out var a))
            {
                string? Get(params string[] keys) => keys
                    .Select(k => a.TryGetProperty(k, out var v) ? v.GetString() : null)
                    .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

                var parts = new[]
                {
                    Get("road", "pedestrian", "neighbourhood"),
                    Get("village", "suburb", "quarter"),
                    Get("city", "town", "municipality"),
                    Get("state", "region")
                }.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct();

                var text = string.Join(", ", parts);
                if (!string.IsNullOrWhiteSpace(text)) return text;
            }
        }
        catch { /* fall back to the phone's own geocoder */ }

        // 2) The phone's geocoder
        try
        {
            var placemark = (await Geocoding.Default.GetPlacemarksAsync(latitude, longitude))?.FirstOrDefault();
            if (placemark is null) return null;

            var parts = new[] { placemark.Thoroughfare, placemark.SubLocality, placemark.Locality, placemark.AdminArea }
                .Where(p => !string.IsNullOrWhiteSpace(p));
            return string.Join(", ", parts);
        }
        catch
        {
            return null; // geocoding needs internet; the coordinates are enough
        }
    }

    public async Task<PlaceResult?> SearchPlaceAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return null;
        try
        {
            // countrycodes=ph keeps every result inside the Philippines
            var url = $"search?format=jsonv2&limit=1&countrycodes=ph&q={Uri.EscapeDataString(query.Trim())}";
            using var doc = System.Text.Json.JsonDocument.Parse(await Nominatim.GetStringAsync(url));
            if (doc.RootElement.GetArrayLength() == 0) return null;

            var first = doc.RootElement[0];
            var lat = double.Parse(first.GetProperty("lat").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
            var lon = double.Parse(first.GetProperty("lon").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
            var name = first.TryGetProperty("display_name", out var d) ? d.GetString() ?? query : query;

            // Shorten "Science City of Muñoz, Nueva Ecija, Central Luzon, 3119, Philippines" to the first 2 parts
            name = string.Join(", ", name.Split(',', StringSplitOptions.TrimEntries).Take(2));
            var point = new GeoPoint(lat, lon);
            return Philippines.Contains(point) ? new PlaceResult(point, name) : null;
        }
        catch
        {
            return null; // no internet / service busy
        }
    }

    private static string F(double v) => v.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public interface IMediaService
{
    Task<PhotoItem?> TakePhotoAsync();
    Task<PhotoItem?> PickPhotoAsync();
}

public class MediaService : IMediaService
{
    public async Task<PhotoItem?> TakePhotoAsync()
    {
        if (!MediaPicker.Default.IsCaptureSupported) return await PickPhotoAsync();

        var status = await Permissions.RequestAsync<Permissions.Camera>();
        if (status != PermissionStatus.Granted) return null;

        return await SaveLocalAsync(await MediaPicker.Default.CapturePhotoAsync());
    }

    public async Task<PhotoItem?> PickPhotoAsync()
    {
        var picked = await MediaPicker.Default.PickPhotosAsync(new MediaPickerOptions { SelectionLimit = 1 });
        return await SaveLocalAsync(picked?.FirstOrDefault());
    }

    /// <summary>Copies the photo into app cache so we can preview it and upload it later.</summary>
    private static async Task<PhotoItem?> SaveLocalAsync(FileResult? file)
    {
        if (file is null) return null;

        var ext = Path.GetExtension(file.FileName);
        if (string.IsNullOrEmpty(ext)) ext = ".jpg";
        var localPath = Path.Combine(FileSystem.CacheDirectory, $"{Guid.NewGuid():N}{ext}");

        await using (var source = await file.OpenReadAsync())
        await using (var target = File.OpenWrite(localPath))
            await source.CopyToAsync(target);

        // FUTURE: compress/resize before upload (e.g. SkiaSharp) to save mobile data.
        return new PhotoItem
        {
            FilePath = localPath,
            FileName = Path.GetFileName(localPath),
            ContentType = string.IsNullOrEmpty(file.ContentType) ? GuessContentType(ext) : file.ContentType
        };
    }

    private static string GuessContentType(string ext) => ext.ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".heic" => "image/heic",
        _ => "image/jpeg"
    };
}

/// <summary>
/// Simple grid-based marker clustering. MAUI Maps has no built-in clustering,
/// so nearby pins are merged into one "N reports" pin depending on the zoom level.
/// </summary>
public static class MarkerClusterer
{
    // ============================================================
    // TODO(MEMBER 3): Marker clustering
    // ------------------------------------------------------------
    // Right now every report becomes its own pin, so a busy area is unreadable when zoomed out.
    // Goal: merge reports that are close together into ONE pin labelled "N reports here".
    //   1. Pick a grid cell size from the zoom level, e.g. cellDeg = visibleRadiusKm / 111.0 / 6.0
    //      (if visibleRadiusKm < 1.5 don't cluster at all).
    //   2. GroupBy each report's cell: (Math.Floor(lat / cellDeg), Math.Floor(lng / cellDeg)).
    //   3. Groups with 1 report → ToPin(report).
    //      Groups with more → new MapPinItem { Location = average lat/lng, Label = $"{count} reports here",
    //                                         Address = highest severity, Count = count } (ReportId stays null).
    //   MapViewModel.OpenPinAsync already zooms in when a cluster pin (ReportId == null) is tapped.
    // ============================================================
    public static List<MapPinItem> Cluster(IReadOnlyList<ReportSummaryDto> reports, double visibleRadiusKm)
        => reports.Select(ToPin).ToList();

    private static MapPinItem ToPin(ReportSummaryDto r) => new()
    {
        Location = new Location(r.Latitude, r.Longitude),
        Label = string.IsNullOrWhiteSpace(r.Title) ? "Animal report" : r.Title,
        Address = $"{r.AnimalType} · {r.NeedText}",
        ReportId = r.Id,
        Name = string.IsNullOrWhiteSpace(r.Title) ? "Animal report" : r.Title,
        Subtitle = r.Subtitle,
        NeedText = Needs.MapLabel(r.Need),
        ConditionText = r.NeedText,
        NeedColor = r.NeedColor,
        ThumbnailUrl = r.ThumbnailUrl
    };
}
