using PawRescue.Mobile.ViewModels;

namespace PawRescue.Mobile.Views;

public partial class NotificationSettingsPage : BasePage
{
    public NotificationSettingsPage(NotificationSettingsViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
