namespace PawRescue.Mobile;

public partial class App : Application
{
    public App()
    {
        InitializeComponent(); // loads Colors.xaml / Styles.xaml FIRST
        UserAppTheme = AppTheme.Light; // the design is light-only
    }

    // AppShell is created here (after the resources above are loaded),
    // otherwise its StaticResource lookups like "BaseShell" fail.
    protected override Window CreateWindow(IActivationState? activationState) => new(new AppShell());
}
