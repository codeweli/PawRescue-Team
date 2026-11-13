using PawRescue.Mobile.ViewModels;

namespace PawRescue.Mobile.Views;

public partial class SplashPage : BasePage
{
    public SplashPage(SplashViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    protected override async void OnAppearing()
    {
        // Small "pop" animation — purely visual, so it lives in the View.
        LogoCircle.Scale = 0.6;
        _ = LogoCircle.ScaleToAsync(1, 450, Easing.SpringOut);
        base.OnAppearing();
        await Task.CompletedTask;
    }
}
