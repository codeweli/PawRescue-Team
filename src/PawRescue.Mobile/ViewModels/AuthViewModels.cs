using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PawRescue.Mobile.Models;
using PawRescue.Mobile.Services;

namespace PawRescue.Mobile.ViewModels;

public partial class SplashViewModel : BaseViewModel
{
    private readonly IAuthApi _auth;
    public SplashViewModel(IAuthApi auth, IDialogService dialogs) : base(dialogs) => _auth = auth;

    public override async Task OnAppearingAsync()
    {
        var minimumSplash = Task.Delay(1200); // let the brand animation play
        var restored = await _auth.TryRestoreSessionAsync();
        await minimumSplash;
        await Shell.Current.GoToAsync(restored ? "//home" : "//login");
    }
}

public partial class LoginViewModel : BaseViewModel
{
    private readonly IAuthApi _auth;

    public LoginViewModel(IAuthApi auth, IDialogService dialogs) : base(dialogs)
    {
        _auth = auth;
        Title = "Welcome back";
    }

    [ObservableProperty] private string email = string.Empty;
    [ObservableProperty] private string password = string.Empty;
    [ObservableProperty] private bool isPasswordHidden = true;

    [RelayCommand]
    private void TogglePassword() => IsPasswordHidden = !IsPasswordHidden;

    [RelayCommand]
    private Task LoginAsync() => RunAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Password))
        {
            await Dialogs.AlertAsync("Missing info", "Please enter your email and password.");
            return;
        }

        await _auth.LoginAsync(Email, Password);
        Password = string.Empty;
        await Shell.Current.GoToAsync("//home");
    });

    [RelayCommand] private Task GoToRegisterAsync() => Shell.Current.GoToAsync("register");
    [RelayCommand] private Task GoToForgotPasswordAsync() => Shell.Current.GoToAsync("forgot");
}

public partial class RegisterViewModel : BaseViewModel
{
    private readonly IAuthApi _auth;

    public RegisterViewModel(IAuthApi auth, IDialogService dialogs) : base(dialogs)
    {
        _auth = auth;
        Title = "Create account";
    }

    [ObservableProperty] private string fullName = string.Empty;
    [ObservableProperty] private string email = string.Empty;
    [ObservableProperty] private string phoneNumber = string.Empty;
    [ObservableProperty] private string password = string.Empty;
    [ObservableProperty] private string confirmPassword = string.Empty;
    [ObservableProperty] private bool registerAsVolunteer;
    [ObservableProperty] private bool acceptedTerms;

    [RelayCommand]
    private Task RegisterAsync() => RunAsync(async () =>
    {
        var error = Validate();
        if (error is not null)
        {
            await Dialogs.AlertAsync("Please check your details", error);
            return;
        }

        await _auth.RegisterAsync(new RegisterRequest(FullName.Trim(), Email.Trim(), Password,
            string.IsNullOrWhiteSpace(PhoneNumber) ? null : PhoneNumber.Trim(), RegisterAsVolunteer));
        await Shell.Current.GoToAsync("//home");
    });

    [RelayCommand] private Task GoBackAsync() => Shell.Current.GoToAsync("..");

    private string? Validate()
    {
        if (string.IsNullOrWhiteSpace(FullName)) return "Full name is required.";
        if (!Regex.IsMatch(Email ?? string.Empty, @"^[^@\s]+@[^@\s]+\.[^@\s]+$")) return "Enter a valid email address.";
        if (Password.Length < 8) return "Password must be at least 8 characters.";
        if (!Password.Any(char.IsDigit) || !Password.Any(char.IsLetter)) return "Password must contain letters and numbers.";
        if (Password != ConfirmPassword) return "Passwords do not match.";
        if (!AcceptedTerms) return "Please accept the terms to continue.";
        return null;
    }
}

public partial class ForgotPasswordViewModel : BaseViewModel
{
    private readonly IAuthApi _auth;

    public ForgotPasswordViewModel(IAuthApi auth, IDialogService dialogs) : base(dialogs)
    {
        _auth = auth;
        Title = "Reset password";
    }

    [ObservableProperty] private string email = string.Empty;
    [ObservableProperty] private string code = string.Empty;
    [ObservableProperty] private string newPassword = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRequestStep))]
    private bool isCodeSent;

    public bool IsRequestStep => !IsCodeSent;

    // TODO(MEMBER 1): Call _auth.ForgotPasswordAsync(Email) inside RunAsync(...).
    //   - Validate that Email isn't empty first (show Dialogs.AlertAsync if it is).
    //   - On success set IsCodeSent = true (this shows the code + new-password fields)
    //     and tell the user to check their email.
    [RelayCommand]
    private Task SendCodeAsync()
        => RunAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(Email))
            {
                await Dialogs.AlertAsync("Missing info", "Please enter your email address.");
                return;
            }
            await _auth.ForgotPasswordAsync(Email.Trim());
            IsCodeSent = true;
            await Dialogs.AlertAsync("Code sent", "Check your email for the 6-digit reset code.");
        });

    // TODO(MEMBER 1): Call _auth.ResetPasswordAsync(Email, Code, NewPassword) inside RunAsync(...).
    //   - Code must be 6 digits and NewPassword at least 8 characters.
    //   - On success show "Password changed" and go back: await Shell.Current.GoToAsync("..");
    [RelayCommand]
    private Task ResetAsync()
        => RunAsync(async () =>
        {
            if (!Regex.IsMatch(Code ?? string.Empty, "^\\d{6}$"))
            {
                await Dialogs.AlertAsync("Invalid code", "Enter the 6-digit code from your email.");
                return;
            }
            if (string.IsNullOrWhiteSpace(NewPassword) || NewPassword.Length < 8)
            {
                await Dialogs.AlertAsync("Invalid password", "Your new password must be at least 8 characters.");
                return;
            }
            await _auth.ResetPasswordAsync(Email.Trim(), Code.Trim(), NewPassword);
            await Dialogs.AlertAsync("Password changed", "Your password has been reset. Please log in.");
            await Shell.Current.GoToAsync("..");
        });
}
