using System.Globalization;
using System.Text.Json;

namespace PawRescue.Mobile.Services;

/// <summary>A driving route: the road shape to draw on the map, plus distance and time.</summary>
public record RouteResult(IReadOnlyList<GeoPoint> Points, double DistanceKm, double DurationMinutes)
{
    public string Summary => $"{DistanceKm:0.0} km · about {Math.Max(1, Math.Round(DurationMinutes))} min drive";
}

public interface IDirectionsService
{
    /// <summary>Gets a road route from A to B (free OSRM service, no API key). Null if no route/no internet.</summary>
    Task<RouteResult?> GetRouteAsync(GeoPoint from, GeoPoint to);

    /// <summary>Opens turn-by-turn navigation in Google Maps (app or browser) from "from" to the exact coordinates.</summary>
    Task OpenNavigationAsync(GeoPoint? from, GeoPoint to);
}

public class DirectionsService : IDirectionsService
{
    private static readonly HttpClient Osrm = CreateClient();

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { BaseAddress = new Uri("https://router.project-osrm.org/"), Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("PawRescue/0.1 (student capstone app)");
        return http;
    }

    public async Task<RouteResult?> GetRouteAsync(GeoPoint from, GeoPoint to)
    {
        try
        {
            // OSRM wants "longitude,latitude" pairs.
            var url = $"route/v1/driving/{F(from.Longitude)},{F(from.Latitude)};{F(to.Longitude)},{F(to.Latitude)}" +
                      "?overview=full&geometries=geojson";
            using var doc = JsonDocument.Parse(await Osrm.GetStringAsync(url));
            var root = doc.RootElement;
            if (root.GetProperty("code").GetString() != "Ok") return null;

            var route = root.GetProperty("routes")[0];
            var points = route.GetProperty("geometry").GetProperty("coordinates")
                              .EnumerateArray()
                              .Select(c => new GeoPoint(c[1].GetDouble(), c[0].GetDouble()))
                              .ToList();
            if (points.Count < 2) return null;

            return new RouteResult(points,
                route.GetProperty("distance").GetDouble() / 1000.0,
                route.GetProperty("duration").GetDouble() / 60.0);
        }
        catch
        {
            return null; // no internet, or the free demo server is busy
        }
    }

    public async Task OpenNavigationAsync(GeoPoint? from, GeoPoint to)
    {
        // Google Maps "directions" URL: free, no API key. Uses EXACT coordinates (never the animal's name,
        // which Google would search as a place — e.g. "Luna" is also a town in La Union).
        // If we know where you are (inside the Philippines) → route from there.
        // If not → just show the exact pin, instead of letting Google route from the phone's own
        // (possibly wrong, e.g. emulator = California) location.
        var url = from is not null
            ? $"https://www.google.com/maps/dir/?api=1&origin={F(from.Latitude)},{F(from.Longitude)}" +
              $"&destination={F(to.Latitude)},{F(to.Longitude)}&travelmode=driving"
            : $"https://www.google.com/maps/search/?api=1&query={F(to.Latitude)},{F(to.Longitude)}";

        await Launcher.Default.OpenAsync(new Uri(url));
    }

    private static string F(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);
}
