using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PawRescue.Mobile.Models;
using PawRescue.Mobile.Services;

namespace PawRescue.Mobile.ViewModels;

public partial class VolunteerDashboardViewModel : BaseViewModel
{
    private readonly IRescueApi _rescues;
    private readonly ILocationService _location;

    public VolunteerDashboardViewModel(IRescueApi rescues, ILocationService location, IDialogService dialogs) : base(dialogs)
    {
        _rescues = rescues;
        _location = location;
        Title = "Volunteer Dashboard";
    }

    public ObservableCollection<ReportSummaryDto> AvailableCases { get; } = new();
    public ObservableCollection<AssignmentDto> MyActiveRescues { get; } = new();

    [ObservableProperty] private int completedCount;
    [ObservableProperty] private int activeCount;
    [ObservableProperty] private int availableCount;
    [ObservableProperty] private bool isRefreshing;
    [ObservableProperty] private ReportSummaryDto? selectedCase;
    [ObservableProperty] private AssignmentDto? selectedAssignment;

    public override Task OnAppearingAsync() => LoadAsync();

    [RelayCommand]
    private async Task LoadAsync()
    {
        await RunAsync(async () =>
        {
            var here = await _location.GetCurrentAsync();
            var available = await _rescues.GetAvailableAsync(here?.Latitude, here?.Longitude);
            var mine = await _rescues.GetMineAsync(activeOnly: false);

            AvailableCases.Clear();
            foreach (var c in available) AvailableCases.Add(c);

            MyActiveRescues.Clear();
            foreach (var a in mine.Where(a => a.Status == AssignmentStatus.Active)) MyActiveRescues.Add(a);

            AvailableCount = available.Count;
            ActiveCount = MyActiveRescues.Count;
            CompletedCount = mine.Count(a => a.Status == AssignmentStatus.Completed);
        }, showBusy: !IsRefreshing);
        IsRefreshing = false;
    }

    partial void OnSelectedCaseChanged(ReportSummaryDto? value)
    {
        if (value is null) return;
        SelectedCase = null;
        _ = Shell.Current.GoToAsync("reportdetails", new Dictionary<string, object> { ["id"] = value.Id });
    }

    partial void OnSelectedAssignmentChanged(AssignmentDto? value)
    {
        if (value is null) return;
        SelectedAssignment = null;
        _ = Shell.Current.GoToAsync("reportdetails", new Dictionary<string, object> { ["id"] = value.ReportId });
    }
}

public partial class NotificationsViewModel : BaseViewModel
{
    private readonly INotificationApi _notifications;

    public NotificationsViewModel(INotificationApi notifications, IDialogService dialogs) : base(dialogs)
    {
        _notifications = notifications;
        Title = "Notifications";
    }

    public ObservableCollection<NotificationDto> Items { get; } = new();

    [ObservableProperty] private bool isRefreshing;
    [ObservableProperty] private bool isEmpty;
    [ObservableProperty] private NotificationDto? selectedItem;

    public override Task OnAppearingAsync() => LoadAsync();

    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            await RunAsync(async () =>
            {
                var page = await _notifications.GetAsync();
                Items.Clear();
                foreach (var item in page.Items)
                    Items.Add(item);
                IsEmpty = Items.Count == 0;
            }, showBusy: !IsRefreshing);
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    [RelayCommand]
    private Task MarkAllReadAsync() => RunAsync(async () =>
    {
        await _notifications.MarkAllAsReadAsync();
        for (var i = 0; i < Items.Count; i++)
        {
            if (!Items[i].IsRead)
                Items[i] = Items[i] with { IsRead = true };
        }
    });

    partial void OnSelectedItemChanged(NotificationDto? value)
    {
        if (value is null) return;
        SelectedItem = null;
        _ = OpenAsync(value);
    }

    private async Task OpenAsync(NotificationDto n)
    {
        if (!n.IsRead)
        {
            for (var i = 0; i < Items.Count; i++)
            {
                if (Items[i].Id == n.Id)
                {
                    Items[i] = Items[i] with { IsRead = true };
                    break;
                }
            }

            await RunAsync(() => _notifications.MarkAsReadAsync(n.Id), showBusy: false);
        }

        if (n.ReportId.HasValue)
        {
            await Shell.Current.GoToAsync("reportdetails", new Dictionary<string, object> { ["id"] = n.ReportId.Value });
        }
    }
}

public partial class ProfileViewModel : BaseViewModel
{
    private readonly IUserApi _users;
    private readonly IReportApi _reports;
    private readonly IAuthApi _auth;
    private readonly TokenStore _tokens;
    private readonly RescueCasesViewModel _rescueCases;

    public ProfileViewModel(IUserApi users, IReportApi reports, IAuthApi auth, TokenStore tokens,
                            RescueCasesViewModel rescueCases, IDialogService dialogs) : base(dialogs)
    {
        _users = users;
        _reports = reports;
        _auth = auth;
        _tokens = tokens;
        _rescueCases = rescueCases;
        Title = "Profile";
    }

    public ObservableCollection<ReportSummaryDto> History { get; } = new();

    [ObservableProperty] private ProfileDto? profile;
    [ObservableProperty] private string initials = "?";
    [ObservableProperty] private bool isEditing;
    [ObservableProperty] private string editFullName = string.Empty;
    [ObservableProperty] private string editPhone = string.Empty;
    [ObservableProperty] private bool isVolunteer;
    [ObservableProperty] private bool isAdmin;
    [ObservableProperty] private ReportSummaryDto? selectedReport;
    [ObservableProperty] private string roleText = string.Empty;

    public override Task OnAppearingAsync() => LoadAsync();

    [RelayCommand]
    private Task LoadAsync() => RunAsync(async () =>
    {
        Profile = await _users.GetProfileAsync();
        RoleText = Profile.User.Role switch
        {
            Roles.Volunteer => "Community volunteer",
            Roles.Administrator => "Administrator",
            _ => "Community member"
        };
        Initials = string.Concat(Profile.User.FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(p => char.ToUpper(p[0])));
        IsVolunteer = Profile.User.Role is Roles.Volunteer or Roles.Administrator;
        IsAdmin = Profile.User.Role == Roles.Administrator;

        History.Clear();
        foreach (var r in await _reports.GetMineAsync()) History.Add(r);
    });

    [RelayCommand]
    private void StartEdit()
    {
        if (Profile is null) return;
        EditFullName = Profile.User.FullName;
        EditPhone = Profile.User.PhoneNumber ?? string.Empty;
        IsEditing = true;
    }

    [RelayCommand] private void CancelEdit() => IsEditing = false;

    [RelayCommand]
    private Task SaveAsync() => RunAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(EditFullName)) { await Dialogs.AlertAsync("Name required", "Please enter your name."); return; }
        var user = await _users.UpdateProfileAsync(EditFullName.Trim(), string.IsNullOrWhiteSpace(EditPhone) ? null : EditPhone.Trim());
        await _tokens.UpdateUserAsync(user);
        IsEditing = false;
        Profile = Profile! with { User = user };
    });

    partial void OnSelectedReportChanged(ReportSummaryDto? value)
    {
        if (value is null) return;
        SelectedReport = null;
        _ = Shell.Current.GoToAsync("reportdetails", new Dictionary<string, object> { ["id"] = value.Id });
    }

    [RelayCommand] private Task OpenVolunteerDashboardAsync() => Shell.Current.GoToAsync("volunteer");
    [RelayCommand] private Task OpenAdminDashboardAsync() => Shell.Current.GoToAsync("admin");

    // ----- Profile menu -----
    [RelayCommand]
    private Task OpenMyReportsAsync()
    {
        _rescueCases.ShowMyReports();
        return Shell.Current.GoToAsync("//reports");
    }

    [RelayCommand] private Task OpenNotificationSettingsAsync() => Shell.Current.GoToAsync("notificationsettings");
    [RelayCommand] private Task OpenSafetyTipsAsync() => Shell.Current.GoToAsync("safetytips");

    [RelayCommand]
    private async Task LogoutAsync()
    {
        if (!await Dialogs.ConfirmAsync("Log out?", "You'll need to sign in again.", "Log out")) return;
        await _auth.LogoutAsync();
        await Shell.Current.GoToAsync("//login");
    }
}

public record StatusCount(string Name, int Count);

public partial class AdminDashboardViewModel : BaseViewModel
{
    private readonly IAdminApi _admin;

    public AdminDashboardViewModel(IAdminApi admin, IDialogService dialogs) : base(dialogs)
    {
        _admin = admin;
        Title = "Admin Dashboard";
    }

    public string[] Tabs { get; } = new[] { "Overview", "Users", "Reports", "Activity" };

    public ObservableCollection<UserDto> Users { get; } = new();
    public ObservableCollection<ReportSummaryDto> Reports { get; } = new();
    public ObservableCollection<AuditLogDto> Activity { get; } = new();
    public ObservableCollection<StatusCount> StatusBreakdown { get; } = new();

    [ObservableProperty] private DashboardStatsDto? stats;
    [ObservableProperty] private string searchText = string.Empty;
    [ObservableProperty] private UserDto? selectedUser;
    [ObservableProperty] private ReportSummaryDto? selectedReport;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOverview), nameof(IsUsers), nameof(IsReports), nameof(IsActivity))]
    private string selectedTab = "Overview";

    public bool IsOverview => SelectedTab == "Overview";
    public bool IsUsers => SelectedTab == "Users";
    public bool IsReports => SelectedTab == "Reports";
    public bool IsActivity => SelectedTab == "Activity";

    public override Task OnAppearingAsync() => LoadTabAsync();

    partial void OnSelectedTabChanged(string value)
    {
        SearchText = string.Empty;
        _ = LoadTabAsync();
    }

    [RelayCommand]
    private Task LoadTabAsync() => RunAsync(async () =>
    {
        switch (SelectedTab)
        {
            case "Overview":
                Stats = await _admin.GetStatsAsync();
                StatusBreakdown.Clear();
                foreach (var kv in Stats.ReportsByStatus) StatusBreakdown.Add(new StatusCount(System.Text.RegularExpressions.Regex.Replace(kv.Key, "(?<=[a-z])([A-Z])", " $1"), kv.Value));
                break;
            case "Users":
                Fill(Users, (await _admin.GetUsersAsync(SearchText)).Items);
                break;
            case "Reports":
                Fill(Reports, (await _admin.GetReportsAsync(SearchText)).Items);
                break;
            case "Activity":
                Fill(Activity, (await _admin.GetActivityAsync()).Items);
                break;
        }
    });

    [RelayCommand] private Task SearchAsync() => LoadTabAsync();

    partial void OnSelectedUserChanged(UserDto? value)
    {
        if (value is null) return;
        SelectedUser = null;
        _ = ManageUserAsync(value);
    }

    partial void OnSelectedReportChanged(ReportSummaryDto? value)
    {
        if (value is null) return;
        SelectedReport = null;
        _ = Shell.Current.GoToAsync("reportdetails", new Dictionary<string, object> { ["id"] = value.Id });
    }

    /// <summary>Admin taps a user → choose a new role or activate/deactivate the account.</summary>
    private async Task ManageUserAsync(UserDto user)
    {
        var toggle = user.IsActive ? "Deactivate account" : "Activate account";
        var choice = await Dialogs.ActionSheetAsync($"{user.FullName} ({user.Role})",
            toggle, "Make User", "Make Volunteer", "Make Administrator");
        if (choice is null) return;   // cancelled

        await RunAsync(async () =>
        {
            UserDto updated = choice switch
            {
                "Make User" => await _admin.ChangeRoleAsync(user.Id, Roles.User),
                "Make Volunteer" => await _admin.ChangeRoleAsync(user.Id, Roles.Volunteer),
                "Make Administrator" => await _admin.ChangeRoleAsync(user.Id, Roles.Administrator),
                _ => await _admin.SetActiveAsync(user.Id, !user.IsActive)
            };

            // Replace the old row so the list shows the new role / status right away.
            var index = Users.IndexOf(user);
            if (index >= 0) Users[index] = updated;
        });
    }

    private static void Fill<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var i in items) target.Add(i);
    }
}
