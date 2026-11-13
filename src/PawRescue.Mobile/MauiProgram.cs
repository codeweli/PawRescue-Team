using Microsoft.Extensions.Logging;
using SkiaSharp.Views.Maui.Controls.Hosting;
using PawRescue.Mobile.Services;
using PawRescue.Mobile.ViewModels;
using PawRescue.Mobile.Views;

namespace PawRescue.Mobile;

/// <summary>Composition root: every dependency of the app is registered here (DEPENDENCY INJECTION).</summary>
public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseSkiaSharp(); // required by Mapsui (OpenStreetMap). Without it the Map tab crashes.

#if DEBUG
        builder.Logging.AddDebug();
#endif


        var s = builder.Services;

        // Core infrastructure
        s.AddSingleton<TokenStore>();
        s.AddTransient<AuthHeaderHandler>();
        s.AddHttpClient<ApiClient>(c =>
        {
            c.BaseAddress = new Uri(ApiConfig.BaseUrl);
            c.Timeout = TimeSpan.FromSeconds(60); // photo uploads on slow mobile data
        }).AddHttpMessageHandler<AuthHeaderHandler>();

        s.AddSingleton<IDialogService, DialogService>();
        s.AddSingleton<ILocationService, LocationService>();
        s.AddSingleton<IMediaService, MediaService>();
        s.AddSingleton<IAppSettings, AppSettings>();
        s.AddSingleton<IDirectionsService, DirectionsService>();

        // API services (interfaces → implementations)
        s.AddTransient<IAuthApi, AuthApi>();
        s.AddTransient<IReportApi, ReportApi>();
        s.AddTransient<IRescueApi, RescueApi>();
        s.AddTransient<INotificationApi, NotificationApi>();
        s.AddTransient<IUserApi, UserApi>();
        s.AddTransient<IAdminApi, AdminApi>();

        // Shell
        s.AddSingleton<AppShell>();

        // Pages + ViewModels. Tab pages are singletons so they keep their state when switching tabs.
        s.AddTransient<SplashPage>().AddTransient<SplashViewModel>();
        s.AddTransient<LoginPage>().AddTransient<LoginViewModel>();
        s.AddTransient<RegisterPage>().AddTransient<RegisterViewModel>();
        s.AddTransient<ForgotPasswordPage>().AddTransient<ForgotPasswordViewModel>();
        s.AddSingleton<HomePage>().AddSingleton<HomeViewModel>();
        s.AddSingleton<MapPage>().AddSingleton<MapViewModel>();
        s.AddSingleton<RescueCasesPage>().AddSingleton<RescueCasesViewModel>();
        s.AddTransient<ReportAnimalPage>().AddTransient<ReportAnimalViewModel>();
        s.AddTransient<SafetyTipsPage>();
        s.AddTransient<NotificationSettingsPage>().AddTransient<NotificationSettingsViewModel>();
        s.AddSingleton<NotificationsPage>().AddSingleton<NotificationsViewModel>();
        s.AddSingleton<ProfilePage>().AddSingleton<ProfileViewModel>();
        s.AddTransient<ReportDetailsPage>().AddTransient<ReportDetailsViewModel>();
        s.AddTransient<VolunteerDashboardPage>().AddTransient<VolunteerDashboardViewModel>();
        s.AddTransient<AdminDashboardPage>().AddTransient<AdminDashboardViewModel>();

        return builder.Build();
    }
}
