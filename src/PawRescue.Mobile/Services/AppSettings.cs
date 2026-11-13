namespace PawRescue.Mobile.Services;

/// <summary>
/// Small on-device settings (Profile → Notification settings).
/// Stored with MAUI Preferences, so they survive app restarts but stay on this phone.
/// </summary>
public interface IAppSettings
{
    /// <summary>Volunteers: share your location so the server can alert you about nearby reports.</summary>
    bool NearbyAlertsEnabled { get; set; }

    /// <summary>How far the Home screen looks for animals that need help.</summary>
    double HomeRadiusKm { get; set; }
}

public class AppSettings : IAppSettings
{
    public bool NearbyAlertsEnabled
    {
        get => Preferences.Default.Get("nearby_alerts", true);
        set => Preferences.Default.Set("nearby_alerts", value);
    }

    public double HomeRadiusKm
    {
        get => Preferences.Default.Get("home_radius_km", 10.0);
        set => Preferences.Default.Set("home_radius_km", value);
    }
}
