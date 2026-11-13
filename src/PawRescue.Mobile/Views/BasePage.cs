using PawRescue.Mobile.ViewModels;

namespace PawRescue.Mobile.Views;

/// <summary>
/// Every screen inherits this: it forwards OnAppearing to the ViewModel,
/// so pages contain no logic of their own (MVVM).
///
/// It also fixes the "content under the status bar" overlap on Android 15+:
/// those phones draw apps edge-to-edge, so pages WITHOUT a navigation bar
/// (Home, Map, Reports, Alerts, Profile) get top padding equal to the status bar height.
/// </summary>
public class BasePage : ContentPage
{
    private bool _insetApplied;

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        ApplyStatusBarInset();
        if (BindingContext is BaseViewModel vm)
            await vm.OnAppearingAsync();
    }

    private void ApplyStatusBarInset()
    {
        if (_insetApplied || Shell.GetNavBarIsVisible(this)) return;
        _insetApplied = true;

        var top = StatusBarHeight();
        if (top > 0)
            Padding = new Thickness(Padding.Left, Padding.Top + top, Padding.Right, Padding.Bottom);
    }

    private static double StatusBarHeight()
    {
#if ANDROID
        // Older Android versions already keep the app below the status bar.
        if (!OperatingSystem.IsAndroidVersionAtLeast(35)) return 0;

        var resources = Android.App.Application.Context.Resources;
        var id = resources?.GetIdentifier("status_bar_height", "dimen", "android") ?? 0;
        if (id <= 0) return 24;
        var px = resources!.GetDimensionPixelSize(id);
        return px / DeviceDisplay.Current.MainDisplayInfo.Density;
#else
        return 0;
#endif
    }
}
