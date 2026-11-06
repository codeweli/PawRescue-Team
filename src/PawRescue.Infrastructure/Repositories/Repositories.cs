using Microsoft.EntityFrameworkCore;
using PawRescue.Application.DTOs;
using PawRescue.Application.Interfaces;
using PawRescue.Domain.Entities;
using PawRescue.Domain.Enums;
using PawRescue.Infrastructure.Data;

namespace PawRescue.Infrastructure.Repositories;

public class UserRepository : Repository<User>, IUserRepository
{
    public UserRepository(AppDbContext db) : base(db) { }

    public Task<User?> GetByEmailAsync(string email, CancellationToken ct = default)
    {
        var normalized = email.Trim().ToLowerInvariant();
        return Set.Include(u => u.Role).FirstOrDefaultAsync(u => u.Email == normalized, ct);
    }

    public Task<User?> GetWithRoleAsync(Guid id, CancellationToken ct = default)
        => Set.Include(u => u.Role).FirstOrDefaultAsync(u => u.Id == id, ct);

    public Task<bool> EmailExistsAsync(string email, CancellationToken ct = default)
    {
        var normalized = email.Trim().ToLowerInvariant();
        return Set.AnyAsync(u => u.Email == normalized, ct);
    }

    public async Task<IReadOnlyList<User>> GetVolunteersInBoxAsync(double minLat, double maxLat, double minLon, double maxLon, CancellationToken ct = default)
        => await Set.Where(u => u.RoleId == RoleIds.Volunteer && u.IsActive
                             && u.LastLatitude >= minLat && u.LastLatitude <= maxLat
                             && u.LastLongitude >= minLon && u.LastLongitude <= maxLon)
                    .Take(200)
                    .ToListAsync(ct);

    public async Task<(IReadOnlyList<User> Items, int Total)> SearchAsync(string? keyword, int page, int pageSize, CancellationToken ct = default)
    {
        var q = Set.Include(u => u.Role).AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var pattern = $"%{keyword.Trim()}%";
            q = q.Where(u => EF.Functions.ILike(u.FullName, pattern) || EF.Functions.ILike(u.Email, pattern));
        }

        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(u => u.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return (items, total);
    }

    public Task<int> CountAsync(int? roleId = null, CancellationToken ct = default)
        => roleId is null ? Set.CountAsync(ct) : Set.CountAsync(u => u.RoleId == roleId, ct);
}

public class RoleRepository : IRoleRepository
{
    private readonly AppDbContext _db;
    public RoleRepository(AppDbContext db) => _db = db;

    public Task<Role?> GetByNameAsync(string name, CancellationToken ct = default)
        => _db.Roles.FirstOrDefaultAsync(r => r.Name.ToLower() == name.ToLower(), ct);
}

public class ReportRepository : Repository<AnimalReport>, IReportRepository
{
    private static readonly ReportStatus[] ClosedStates = { ReportStatus.Rescued, ReportStatus.Closed };

    public ReportRepository(AppDbContext db) : base(db) { }

    private IQueryable<AnimalReport> WithListIncludes() =>
        Set.Include(r => r.Location).Include(r => r.Images);

    public Task<AnimalReport?> GetDetailsAsync(Guid id, CancellationToken ct = default)
        => Set.Include(r => r.Location)
              .Include(r => r.Images)
              .Include(r => r.Reporter)
              .Include(r => r.Assignments).ThenInclude(a => a.Volunteer)
              .AsSplitQuery()
              .FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task<IReadOnlyList<AnimalReport>> GetInBoundingBoxAsync(
        double minLat, double maxLat, double minLon, double maxLon,
        SeverityLevel? minSeverity, ReportStatus? status, bool openOnly, int limit, CancellationToken ct = default)
    {
        var q = WithListIncludes().AsNoTracking()
            .Where(r => r.Location.Latitude >= minLat && r.Location.Latitude <= maxLat
                     && r.Location.Longitude >= minLon && r.Location.Longitude <= maxLon);

        // Apply map filters
        if (minSeverity is not null)
            q = q.Where(r => r.Severity >= minSeverity);

        if (status is not null)
            q = q.Where(r => r.Status == status);
        else if (openOnly)
            q = q.Where(r => !ClosedStates.Contains(r.Status));

        return await q.OrderByDescending(r => r.PriorityScore).Take(limit).ToListAsync(ct);
    }

    public async Task<(IReadOnlyList<AnimalReport> Items, int Total)> SearchAsync(SearchQuery query, CancellationToken ct = default)
    {
        var q = WithListIncludes().AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var pattern = $"%{query.Keyword.Trim()}%";
            q = q.Where(r => EF.Functions.ILike(r.Title, pattern)
                          || EF.Functions.ILike(r.Description, pattern)
                          || (r.Location.Address != null && EF.Functions.ILike(r.Location.Address, pattern)));
        }
        // Apply search filters
        if (query.AnimalType is not null)
            q = q.Where(r => r.AnimalType == query.AnimalType);

        if (query.Severity is not null)
            q = q.Where(r => r.Severity == query.Severity);

        if (query.Status is not null)
            q = q.Where(r => r.Status == query.Status);

        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(r => r.CreatedAt)
                           .Skip((query.Page - 1) * query.PageSize)
                           .Take(query.PageSize)
                           .ToListAsync(ct);
        return (items, total);
    }

    public async Task<IReadOnlyList<AnimalReport>> GetByReporterAsync(Guid reporterId, CancellationToken ct = default)
        => await WithListIncludes().AsNoTracking()
                 .Where(r => r.ReporterId == reporterId)
                 .OrderByDescending(r => r.CreatedAt)
                 .ToListAsync(ct);

    public async Task<IReadOnlyList<AnimalReport>> GetAvailableForRescueAsync(int limit, CancellationToken ct = default)
        => await WithListIncludes().AsNoTracking()
                 .Where(r => r.Status == ReportStatus.Reported || r.Status == ReportStatus.UnderReview)
                 .OrderByDescending(r => r.PriorityScore).ThenBy(r => r.CreatedAt)
                 .Take(limit)
                 .ToListAsync(ct);

    public Task<int> CountAsync(CancellationToken ct = default) => Set.CountAsync(ct);

    public Task<int> CountOpenAsync(SeverityLevel? severity = null, CancellationToken ct = default)
    {
        var q = Set.Where(r => !ClosedStates.Contains(r.Status));
        if (severity is not null) q = q.Where(r => r.Severity == severity);
        return q.CountAsync(ct);
    }

    public Task<int> CountByReporterAndStatusAsync(Guid reporterId, ReportStatus? status, CancellationToken ct = default)
        => status is null
            ? Set.CountAsync(r => r.ReporterId == reporterId, ct)
            : Set.CountAsync(r => r.ReporterId == reporterId && r.Status == status, ct);

    public Task<int> CountCreatedSinceAsync(DateTime since, CancellationToken ct = default)
        => Set.CountAsync(r => r.CreatedAt >= since, ct);

    public Task<Dictionary<ReportStatus, int>> CountByStatusAsync(CancellationToken ct = default)
        => Set.GroupBy(r => r.Status)
              .Select(g => new { g.Key, Count = g.Count() })
              .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

    public Task<Dictionary<AnimalType, int>> CountByAnimalTypeAsync(CancellationToken ct = default)
        => Set.GroupBy(r => r.AnimalType)
              .Select(g => new { g.Key, Count = g.Count() })
              .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
}

public class RescueAssignmentRepository : Repository<RescueAssignment>, IRescueAssignmentRepository
{
    public RescueAssignmentRepository(AppDbContext db) : base(db) { }

    public Task<RescueAssignment?> GetActiveForReportAsync(Guid reportId, CancellationToken ct = default)
        => Set.Include(a => a.Report).Include(a => a.Volunteer)
              .FirstOrDefaultAsync(a => a.ReportId == reportId && a.Status == AssignmentStatus.Active, ct);

    public async Task<IReadOnlyList<RescueAssignment>> GetByVolunteerAsync(Guid volunteerId, AssignmentStatus? status, CancellationToken ct = default)
    {
        var q = Set.Include(a => a.Report).Include(a => a.Volunteer).AsNoTracking()
                   .Where(a => a.VolunteerId == volunteerId);
        if (status is not null) q = q.Where(a => a.Status == status);
        return await q.OrderByDescending(a => a.AssignedAt).ToListAsync(ct);
    }

    public Task<int> CountByVolunteerAsync(Guid volunteerId, AssignmentStatus status, CancellationToken ct = default)
        => Set.CountAsync(a => a.VolunteerId == volunteerId && a.Status == status, ct);
}

public class NotificationRepository : Repository<Notification>, INotificationRepository
{
    public NotificationRepository(AppDbContext db) : base(db) { }

    // TODO(MEMBER 2): Query Set.AsNoTracking() where n.UserId == userId
    //   (+ !n.IsRead when unreadOnly), CountAsync for the total,
    //   then OrderByDescending(CreatedAt).Skip((page-1)*pageSize).Take(pageSize).ToListAsync(ct).
    //   Hint: copy the pattern from AuditLogRepository / UserRepository.SearchAsync.
    public Task<(IReadOnlyList<Notification> Items, int Total)> GetForUserAsync(Guid userId, bool unreadOnly, int page, int pageSize, CancellationToken ct = default)
        => throw new NotImplementedException("TODO(MEMBER 2)");

    // TODO(MEMBER 2): Set.CountAsync(n => n.UserId == userId && !n.IsRead, ct)
    public Task<int> CountUnreadAsync(Guid userId, CancellationToken ct = default)
        => throw new NotImplementedException("TODO(MEMBER 2)");

    // TODO(MEMBER 2): Load the user's unread notifications (tracked, no AsNoTracking)
    //   and call n.MarkAsRead() on each. The service saves afterwards.
    public Task MarkAllAsReadAsync(Guid userId, CancellationToken ct = default)
        => throw new NotImplementedException("TODO(MEMBER 2)");
}

public class RefreshTokenRepository : Repository<RefreshToken>, IRefreshTokenRepository
{
    public RefreshTokenRepository(AppDbContext db) : base(db) { }

    public Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken ct = default)
        => Set.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

    public async Task RevokeAllForUserAsync(Guid userId, CancellationToken ct = default)
    {
        var active = await Set.Where(t => t.UserId == userId && t.RevokedAt == null).ToListAsync(ct);
        foreach (var t in active) t.Revoke();
    }
}

public class AuditLogRepository : Repository<AuditLog>, IAuditLogRepository
{
    public AuditLogRepository(AppDbContext db) : base(db) { }

    // TODO(MEMBER 4): Count all logs, then return the newest page:
    //   Set.Include(l => l.User).AsNoTracking().OrderByDescending(l => l.CreatedAt)
    //      .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct)
    public Task<(IReadOnlyList<AuditLog> Items, int Total)> GetRecentAsync(int page, int pageSize, CancellationToken ct = default)
        => throw new NotImplementedException("TODO(MEMBER 4)");
}
