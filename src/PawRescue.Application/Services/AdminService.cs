using PawRescue.Application.Common;
using PawRescue.Application.DTOs;
using PawRescue.Application.Interfaces;
using PawRescue.Domain.Entities;
using PawRescue.Domain.Enums;

namespace PawRescue.Application.Services;

public class AdminService : IAdminService
{
    private readonly IUserRepository _users;
    private readonly IRoleRepository _roles;
    private readonly IReportRepository _reports;
    private readonly IAuditLogRepository _logs;
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditLogger _audit;

    public AdminService(
        IUserRepository users,
        IRoleRepository roles,
        IReportRepository reports,
        IAuditLogRepository logs,
        IUnitOfWork uow,
        ICurrentUserService currentUser,
        IAuditLogger audit)
    {
        _users = users;
        _roles = roles;
        _reports = reports;
        _logs = logs;
        _uow = uow;
        _currentUser = currentUser;
        _audit = audit;
    }

    public async Task<DashboardStatsDto> GetStatsAsync(
        CancellationToken ct = default)
    {
        var byStatus = await _reports.CountByStatusAsync(ct);
        var byType = await _reports.CountByAnimalTypeAsync(ct);

        return new DashboardStatsDto(
            TotalUsers: await _users.CountAsync(null, ct),
            TotalVolunteers: await _users.CountAsync(RoleIds.Volunteer, ct),
            TotalReports: await _reports.CountAsync(ct),
            OpenReports: await _reports.CountOpenAsync(null, ct),
            RescuedReports: byStatus.GetValueOrDefault(ReportStatus.Rescued),
            CriticalOpenReports: await _reports.CountOpenAsync(SeverityLevel.Critical, ct),
            ReportsLast7Days: await _reports.CountCreatedSinceAsync(DateTime.UtcNow.AddDays(-7), ct),
            ReportsByStatus: Enum.GetValues<ReportStatus>().ToDictionary(s => s.ToString(), s => byStatus.GetValueOrDefault(s)),
            ReportsByAnimalType: Enum.GetValues<AnimalType>().ToDictionary(t => t.ToString(), t => byType.GetValueOrDefault(t)));
    }

    public async Task<PagedResult<UserDto>> GetUsersAsync(
        string? keyword, int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var (items, total) = await _users.SearchAsync(keyword, page, pageSize, ct);

        return new PagedResult<UserDto>(
            items.Select(u => u.ToDto()).ToList(), page, pageSize, total);
    }

    // ============================================================
    // TODO(MEMBER 4): Change a user's role (User / Volunteer / Administrator)
    // ------------------------------------------------------------
    // 1. Load the user with _users.GetWithRoleAsync(userId, ct) → NotFoundException if null.
    // 2. Find the role with _roles.GetByNameAsync(role, ct) → ValidationException("Unknown role") if null.
    // 3. An admin must NOT change their own role: if userId == _currentUser.UserId → ValidationException.
    // 4. user.ChangeRole(newRole.Id); save; audit "USER_ROLE_CHANGED".
    // 5. return user.ToDto() with { Role = newRole.Name };
    // ============================================================
    public async Task<UserDto> ChangeRoleAsync(
        Guid userId, string role, CancellationToken ct = default)
    {
        // Prevent admin from changing their own role
        if (userId == _currentUser.UserId)
            throw new ValidationException("You cannot change your own role.");

        var user = await _users.GetWithRoleAsync(userId, ct);

        if (user == null)
            throw new NotFoundException("User", userId);

        // Get the new role
        var newRole = await _roles.GetByNameAsync(role, ct);

        if (newRole == null)
            throw new ValidationException("Unknown role.");

        var oldRole = user.Role.Name;

        // Update user's role
        user.ChangeRole(newRole.Id);

        await _uow.SaveChangesAsync(ct);

        // Record the role change in the activity log
        await _audit.LogAsync(
            "USER_ROLE_CHANGED",
            nameof(User),
            user.Id.ToString(),
            new { OldRole = oldRole, NewRole = newRole.Name },
            ct);

        return user.ToDto() with { Role = newRole.Name };
    }

    // TODO(MEMBER 4): Activate / deactivate an account.
    //   - Load user (NotFoundException if missing); an admin can't deactivate themselves.
    //   - isActive ? user.Activate() : user.Deactivate(); save.
    //   - Audit "USER_ACTIVATED" or "USER_DEACTIVATED". Return user.ToDto().
    //   (AuthService.LoginAsync already blocks inactive users.)
    public async Task<UserDto> SetActiveAsync(
        Guid userId, bool isActive, CancellationToken ct = default)
    {
        // Find the selected user
        var user = await _users.GetWithRoleAsync(userId, ct);

        if (user == null)
            throw new NotFoundException("User", userId);

        // Prevent admin from deactivating their own account
        if (userId == _currentUser.UserId && !isActive)
            throw new ValidationException("You cannot deactivate your own account.");

        // Activate or deactivate the user account
        if (isActive)
            user.Activate();
        else
            user.Deactivate();

        await _uow.SaveChangesAsync(ct);

        // Record the account status change in the activity log
        await _audit.LogAsync(
            isActive ? "USER_ACTIVATED" : "USER_DEACTIVATED",
            nameof(User),
            user.Id.ToString(),
            new { user.FullName },
            ct);

        return user.ToDto();
    }

    // TODO(MEMBER 4): Activity monitoring.
    //   - Clamp page/pageSize, call _logs.GetRecentAsync(page, pageSize, ct) (write it in AuditLogRepository).
    //   - Map each AuditLog to new AuditLogDto(l.Id, l.User?.FullName, l.Action, l.EntityName, l.EntityId, l.Details, l.CreatedAt).
    public async Task<PagedResult<AuditLogDto>> GetActivityAsync(
        int page, int pageSize, CancellationToken ct = default)
    {
        // Set valid page number and page size limits
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        // Get recent activity logs from the database
        var (items, total) = await _logs.GetRecentAsync(page, pageSize, ct);

        // Convert activity logs into AuditLogDto
        var logs = items.Select(l => new AuditLogDto(
            l.Id,
            l.User?.FullName,
            l.Action,
            l.EntityName,
            l.EntityId,
            l.Details,
            l.CreatedAt
        )).ToList();

        // Return the activity logs with pagination details
        return new PagedResult<AuditLogDto>(logs, page, pageSize, total);
    }
}