using PawRescue.Mobile.Views;

namespace PawRescue.Mobile;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        // Pages pushed on top of the tabs (have a back button)
        Routing.RegisterRoute("register", typeof(RegisterPage));
        Routing.RegisterRoute("forgot", typeof(ForgotPasswordPage));
        Routing.RegisterRoute("reportanimal", typeof(ReportAnimalPage));
        Routing.RegisterRoute("reportdetails", typeof(ReportDetailsPage));
        Routing.RegisterRoute("volunteer", typeof(VolunteerDashboardPage));
        Routing.RegisterRoute("admin", typeof(AdminDashboardPage));
        Routing.RegisterRoute("safetytips", typeof(SafetyTipsPage));
        Routing.RegisterRoute("notificationsettings", typeof(NotificationSettingsPage));
    }
}
