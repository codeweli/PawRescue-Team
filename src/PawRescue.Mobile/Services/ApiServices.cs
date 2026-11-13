using System.Net.Http.Headers;
using System.Globalization;
using PawRescue.Mobile.Models;

namespace PawRescue.Mobile.Services;

// One small interface per feature (Interface Segregation). ViewModels depend on these,
// so a fake implementation can be injected for UI demos or unit tests.

public interface IAuthApi
{
    Task<UserDto> LoginAsync(string email, string password);
    Task<UserDto> RegisterAsync(RegisterRequest request);
    Task ForgotPasswordAsync(string email);
    Task ResetPasswordAsync(string email, string code, string newPassword);
    Task<bool> TryRestoreSessionAsync();
    Task LogoutAsync();
}

public class AuthApi : IAuthApi
{
    private readonly ApiClient _api;
    private readonly TokenStore _tokens;

    public AuthApi(ApiClient api, TokenStore tokens)
    {
        _api = api;
        _tokens = tokens;
    }

    public async Task<UserDto> LoginAsync(string email, string password)
    {
        // FUTURE: pass the FCM device token here once Firebase is wired into the app (see README).
        var auth = await _api.PostAsync<AuthResponse>("api/auth/login", new LoginRequest(email.Trim(), password));
        await _tokens.SaveAsync(auth);
        return auth.User;
    }

    public async Task<UserDto> RegisterAsync(RegisterRequest request)
    {
        var auth = await _api.PostAsync<AuthResponse>("api/auth/register", request);
        await _tokens.SaveAsync(auth);
        return auth.User;
    }

    public Task ForgotPasswordAsync(string email)
        => _api.PostAsync("api/auth/forgot-password", new ForgotPasswordRequest(email.Trim()));

    public Task ResetPasswordAsync(string email, string code, string newPassword)
        => _api.PostAsync("api/auth/reset-password", new ResetPasswordRequest(email.Trim(), code.Trim(), newPassword));

    public async Task<bool> TryRestoreSessionAsync()
    {
        if (!await _tokens.LoadAsync()) return false;
        try
        {
            // Validates the session (the handler refreshes the access token if it expired).
            var profile = await _api.GetAsync<ProfileDto>("api/users/me");
            await _tokens.UpdateUserAsync(profile.User);
            return true;
        }
        catch
        {
            _tokens.Clear();
            return false;
        }
    }

    public async Task LogoutAsync()
    {
        try
        {
            if (_tokens.RefreshToken is not null)
                await _api.PostAsync("api/auth/logout", new RefreshTokenRequest(_tokens.RefreshToken));
        }
        catch { /* logging out locally is enough if the server is unreachable */ }
        _tokens.Clear();
    }
}

public interface IReportApi
{
    Task<ReportDto> CreateAsync(string title, string? description, AnimalType type, AnimalCondition condition,
                                SeverityLevel severity, double lat, double lng, string? address, IEnumerable<PhotoItem> photos);
    Task<ReportDto> GetAsync(Guid id);
    Task<ReportDto> UpdateAsync(Guid id, UpdateReportRequest request);
    Task DeleteAsync(Guid id);
    Task<List<ReportSummaryDto>> GetNearbyAsync(double lat, double lng, double radiusKm,
                                                SeverityLevel? minSeverity = null, ReportStatus? status = null, bool openOnly = true);
    Task<PagedResult<ReportSummaryDto>> SearchAsync(string? keyword, ReportStatus? status = null, SeverityLevel? severity = null, int page = 1);
    Task<List<ReportSummaryDto>> GetMineAsync();
    Task<HealthAnalysisResult> AnalyzeHealthAsync(Guid id);
    Task<CommunityStatsDto> GetCommunityStatsAsync();
}

public class ReportApi : IReportApi
{
    private readonly ApiClient _api;
    public ReportApi(ApiClient api) => _api = api;

    public async Task<ReportDto> CreateAsync(string title, string? description, AnimalType type, AnimalCondition condition,
                                             SeverityLevel severity, double lat, double lng, string? address, IEnumerable<PhotoItem> photos)
    {
        using var form = new MultipartFormDataContent
        {
            { new StringContent(title), "Title" },
            { new StringContent(description ?? string.Empty), "Description" },
            { new StringContent(type.ToString()), "AnimalType" },
            { new StringContent(condition.ToString()), "Condition" },
            { new StringContent(severity.ToString()), "Severity" },
            { new StringContent(lat.ToString(CultureInfo.InvariantCulture)), "Latitude" },
            { new StringContent(lng.ToString(CultureInfo.InvariantCulture)), "Longitude" },
            { new StringContent(address ?? string.Empty), "Address" }
        };

        foreach (var photo in photos)
        {
            var bytes = await File.ReadAllBytesAsync(photo.FilePath);
            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = new MediaTypeHeaderValue(photo.ContentType);
            form.Add(file, "Images", photo.FileName);
        }

        return await _api.PostMultipartAsync<ReportDto>("api/reports", form);
    }

    public Task<ReportDto> GetAsync(Guid id) => _api.GetAsync<ReportDto>($"api/reports/{id}");

    public Task<ReportDto> UpdateAsync(Guid id, UpdateReportRequest request) => _api.PutAsync<ReportDto>($"api/reports/{id}", request);

    public Task DeleteAsync(Guid id) => _api.DeleteAsync($"api/reports/{id}");

    public Task<List<ReportSummaryDto>> GetNearbyAsync(double lat, double lng, double radiusKm,
                                                       SeverityLevel? minSeverity = null, ReportStatus? status = null, bool openOnly = true)
    {
        var url = $"api/map/nearby?latitude={F(lat)}&longitude={F(lng)}&radiusKm={F(radiusKm)}&openOnly={openOnly}";
        if (minSeverity is not null) url += $"&minSeverity={minSeverity}";
        if (status is not null) url += $"&status={status}";
        return _api.GetAsync<List<ReportSummaryDto>>(url);
    }

    public Task<PagedResult<ReportSummaryDto>> SearchAsync(string? keyword, ReportStatus? status = null, SeverityLevel? severity = null, int page = 1)
    {
        var url = $"api/map/search?page={page}&pageSize=50";
        if (!string.IsNullOrWhiteSpace(keyword)) url += $"&keyword={Uri.EscapeDataString(keyword)}";
        if (status is not null) url += $"&status={status}";
        if (severity is not null) url += $"&severity={severity}";
        return _api.GetAsync<PagedResult<ReportSummaryDto>>(url);
    }

    public Task<List<ReportSummaryDto>> GetMineAsync() => _api.GetAsync<List<ReportSummaryDto>>("api/reports/mine");

    public Task<HealthAnalysisResult> AnalyzeHealthAsync(Guid id)
        => _api.PostAsync<HealthAnalysisResult>($"api/reports/{id}/ai-analysis", null);

    public Task<CommunityStatsDto> GetCommunityStatsAsync() => _api.GetAsync<CommunityStatsDto>("api/reports/stats");

    private static string F(double v) => v.ToString(CultureInfo.InvariantCulture);
}

public interface IRescueApi
{
    Task<List<ReportSummaryDto>> GetAvailableAsync(double? lat, double? lng);
    Task<AssignmentDto> AcceptAsync(Guid reportId);
    Task<ReportDto> UpdateStatusAsync(Guid reportId, ReportStatus status, string? note);
    Task<List<AssignmentDto>> GetMineAsync(bool activeOnly);
}

public class RescueApi : IRescueApi
{
    private readonly ApiClient _api;
    public RescueApi(ApiClient api) => _api = api;

    public Task<List<ReportSummaryDto>> GetAvailableAsync(double? lat, double? lng)
    {
        var url = "api/rescues/available";
        if (lat is not null && lng is not null)
            url += $"?latitude={lat.Value.ToString(CultureInfo.InvariantCulture)}&longitude={lng.Value.ToString(CultureInfo.InvariantCulture)}";
        return _api.GetAsync<List<ReportSummaryDto>>(url);
    }

    public Task<AssignmentDto> AcceptAsync(Guid reportId) => _api.PostAsync<AssignmentDto>($"api/rescues/{reportId}/accept", null);

    public Task<ReportDto> UpdateStatusAsync(Guid reportId, ReportStatus status, string? note)
        => _api.PatchAsync<ReportDto>($"api/rescues/{reportId}/status", new UpdateStatusRequest(status, note));

    public Task<List<AssignmentDto>> GetMineAsync(bool activeOnly)
        => _api.GetAsync<List<AssignmentDto>>($"api/rescues/mine?activeOnly={activeOnly}");
}

public interface INotificationApi
{
    Task<PagedResult<NotificationDto>> GetAsync(int page = 1);
    Task<int> GetUnreadCountAsync();
    Task MarkAsReadAsync(Guid id);
    Task MarkAllAsReadAsync();
}

public class NotificationApi : INotificationApi
{
    private readonly ApiClient _api;
    public NotificationApi(ApiClient api) => _api = api;

    private record CountResponse(int Count);

    public Task<PagedResult<NotificationDto>> GetAsync(int page = 1)
        => _api.GetAsync<PagedResult<NotificationDto>>($"api/notifications?page={page}&pageSize=30");

    public async Task<int> GetUnreadCountAsync()
        => (await _api.GetAsync<CountResponse>("api/notifications/unread-count")).Count;

    public Task MarkAsReadAsync(Guid id) => _api.PatchAsync($"api/notifications/{id}/read");
    public Task MarkAllAsReadAsync() => _api.PatchAsync("api/notifications/read-all");
}

public interface IUserApi
{
    Task<ProfileDto> GetProfileAsync();
    Task<UserDto> UpdateProfileAsync(string fullName, string? phone);
    Task UpdateLocationAsync(double lat, double lng);
}

public class UserApi : IUserApi
{
    private readonly ApiClient _api;
    public UserApi(ApiClient api) => _api = api;

    public Task<ProfileDto> GetProfileAsync() => _api.GetAsync<ProfileDto>("api/users/me");

    public Task<UserDto> UpdateProfileAsync(string fullName, string? phone)
        => _api.PutAsync<UserDto>("api/users/me", new UpdateProfileRequest(fullName, phone));

    public Task UpdateLocationAsync(double lat, double lng)
        => _api.PutAsync("api/users/me/location", new UpdateLocationRequest(lat, lng, null));
}

public interface IAdminApi
{
    Task<DashboardStatsDto> GetStatsAsync();
    Task<PagedResult<UserDto>> GetUsersAsync(string? keyword, int page = 1);
    Task<UserDto> ChangeRoleAsync(Guid userId, string role);
    Task<UserDto> SetActiveAsync(Guid userId, bool active);
    Task<PagedResult<ReportSummaryDto>> GetReportsAsync(string? keyword, int page = 1);
    Task<PagedResult<AuditLogDto>> GetActivityAsync(int page = 1);
}

public class AdminApi : IAdminApi
{
    private readonly ApiClient _api;
    public AdminApi(ApiClient api) => _api = api;

    public Task<DashboardStatsDto> GetStatsAsync() => _api.GetAsync<DashboardStatsDto>("api/admin/stats");

    public Task<PagedResult<UserDto>> GetUsersAsync(string? keyword, int page = 1)
        => _api.GetAsync<PagedResult<UserDto>>($"api/admin/users?page={page}&pageSize=30&keyword={Uri.EscapeDataString(keyword ?? string.Empty)}");

    public Task<UserDto> ChangeRoleAsync(Guid userId, string role)
        => _api.PatchAsync<UserDto>($"api/admin/users/{userId}/role", new ChangeRoleRequest(role));

    public Task<UserDto> SetActiveAsync(Guid userId, bool active)
        => _api.PatchAsync<UserDto>($"api/admin/users/{userId}/{(active ? "activate" : "deactivate")}", null);

    public Task<PagedResult<ReportSummaryDto>> GetReportsAsync(string? keyword, int page = 1)
        => _api.GetAsync<PagedResult<ReportSummaryDto>>($"api/admin/reports?page={page}&pageSize=30&keyword={Uri.EscapeDataString(keyword ?? string.Empty)}");

    public Task<PagedResult<AuditLogDto>> GetActivityAsync(int page = 1)
        => _api.GetAsync<PagedResult<AuditLogDto>>($"api/admin/activity?page={page}&pageSize=30");
}
