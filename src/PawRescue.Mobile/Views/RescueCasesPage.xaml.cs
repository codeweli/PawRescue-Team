using PawRescue.Mobile.ViewModels;

namespace PawRescue.Mobile.Views;

public partial class RescueCasesPage : BasePage
{
    public RescueCasesPage(RescueCasesViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
