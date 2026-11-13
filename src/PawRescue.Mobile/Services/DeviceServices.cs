using PawRescue.Mobile.Models;

namespace PawRescue.Mobile.Services;

public record GeoPoint(double Latitude, double Longitude);

public interface ILocationService
{
    /// <summary>Returns the current GPS position, or null if permission was denied / GPS is off.</summary>
    Task<GeoPoint?> GetCurrentAsync();
    Task<string?> GetAddressAsync(double latitude, double longitude);
}

public class LocationService : ILocationService
{
    private GeoPoint? _lastKnown;

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

    public async Task<string?> GetAddressAsync(double latitude, double longitude)
    {
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
        Hue = Needs.HueOf(r.Need),
        Name = string.IsNullOrWhiteSpace(r.Title) ? "Animal report" : r.Title,
        Subtitle = r.Subtitle,
        NeedText = Needs.MapLabel(r.Need),
        ConditionText = r.NeedText,
        NeedColor = r.NeedColor,
        ThumbnailUrl = r.ThumbnailUrl
    };
}
