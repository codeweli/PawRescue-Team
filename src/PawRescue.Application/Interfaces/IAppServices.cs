using PawRescue.Application.DTOs;
using PawRescue.Application.Notifications;
using PawRescue.Domain.Entities;

namespace PawRescue.Application.Interfaces;

// SERVICE LAYER: controllers stay thin and call these; all business rules live behind them.
// INTERFACE SEGREGATION: one small interface per feature area instead of one giant service.

public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default);
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default);
    Task<AuthResponse> RefreshAsync(string refreshToken, CancellationToken ct = default);
    Task LogoutAsync(string refreshToken, CancellationToken ct = default);
    Task ForgotPasswordAsync(string email, CancellationToken ct = default);
    Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct = default);
}

public interface IReportService
{
    Task<ReportDto> CreateAsync(CreateReportRequest request, IReadOnlyList<ImageUpload> images, CancellationToken ct = default);
    Task<ReportDto> UpdateAsync(Guid id, UpdateReportRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<ReportDto> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<ReportSummaryDto>> GetNearbyAsync(NearbyQuery query, CancellationToken ct = default);
    Task<PagedResult<ReportSummaryDto>> SearchAsync(SearchQuery query, CancellationToken ct = default);
    Task<IReadOnlyList<ReportSummaryDto>> GetMyReportsAsync(CancellationToken ct = default);
    Task<HealthAnalysisResult> AnalyzeHealthAsync(Guid id, CancellationToken ct = default);
    Task<CommunityStatsDto> GetCommunityStatsAsync(CancellationToken ct = default);
}

public interface IRescueService
{
    Task<IReadOnlyList<ReportSummaryDto>> GetAvailableCasesAsync(double? latitude, double? longitude, CancellationToken ct = default);
    Task<AssignmentDto> AcceptAsync(Guid reportId, CancellationToken ct = default);
    Task<ReportDto> UpdateStatusAsync(Guid reportId, UpdateStatusRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<AssignmentDto>> GetMyAssignmentsAsync(bool activeOnly, CancellationToken ct = default);
}

public interface INotificationService
{
    Task<PagedResult<NotificationDto>> GetMyNotificationsAsync(bool unreadOnly, int page, int pageSize, CancellationToken ct = default);
    Task<int> GetUnreadCountAsync(CancellationToken ct = default);
    Task MarkAsReadAsync(Guid id, CancellationToken ct = default);
    Task MarkAllAsReadAsync(CancellationToken ct = default);

    // Used internally by other services
    Task NotifyAsync(Guid userId, NotificationTemplate template, CancellationToken ct = default);
    Task NotifyNearbyVolunteersAsync(AnimalReport report, double radiusKm, CancellationToken ct = default);
}

public interface IUserService
{
    Task<ProfileDto> GetMyProfileAsync(CancellationToken ct = default);
    Task<UserDto> UpdateMyProfileAsync(UpdateProfileRequest request, CancellationToken ct = default);
    Task UpdateMyLocationAsync(UpdateLocationRequest request, CancellationToken ct = default);
}

public interface IAdminService
{
    Task<DashboardStatsDto> GetStatsAsync(CancellationToken ct = default);
    Task<PagedResult<UserDto>> GetUsersAsync(string? keyword, int page, int pageSize, CancellationToken ct = default);
    Task<UserDto> ChangeRoleAsync(Guid userId, string role, CancellationToken ct = default);
    Task<UserDto> SetActiveAsync(Guid userId, bool isActive, CancellationToken ct = default);
    Task<PagedResult<AuditLogDto>> GetActivityAsync(int page, int pageSize, CancellationToken ct = default);
}

public interface IAuditLogger
{
    Task LogAsync(string action, string entityName, string? entityId, object? details = null, CancellationToken ct = default);
}
