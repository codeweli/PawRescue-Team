using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PawRescue.Mobile.Services;

namespace PawRescue.Mobile.ViewModels;

/// <summary>Profile → Notification settings.</summary>
public partial class NotificationSettingsViewModel : BaseViewModel
{
    private readonly IAppSettings _settings;

    public NotificationSettingsViewModel(IAppSettings settings, TokenStore tokens, IDialogService dialogs) : base(dialogs)
    {
        _settings = settings;
        Title = "Notification settings";
        IsVolunteer = tokens.IsVolunteer;
        nearbyAlertsEnabled = settings.NearbyAlertsEnabled;
        selectedRadius = $"{settings.HomeRadiusKm:0} km";
        if (!RadiusOptions.Contains(selectedRadius)) selectedRadius = "10 km";
    }

    public bool IsVolunteer { get; }

    public string[] RadiusOptions { get; } = { "2 km", "5 km", "10 km", "25 km" };

    [ObservableProperty] private bool nearbyAlertsEnabled;
    [ObservableProperty] private string selectedRadius;

    partial void OnNearbyAlertsEnabledChanged(bool value) => _settings.NearbyAlertsEnabled = value;

    partial void OnSelectedRadiusChanged(string value)
    {
        if (double.TryParse(value.Split(' ')[0], out var km))
            _settings.HomeRadiusKm = km;
    }

    /// <summary>Opens the phone's settings for this app (to allow/block push notifications).</summary>
    [RelayCommand]
    private void OpenPhoneSettings() => AppInfo.Current.ShowSettingsUI();
}
