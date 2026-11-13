using Microsoft.Extensions.Logging;
using PawRescue.Application.Common;
using PawRescue.Application.DTOs;
using PawRescue.Application.Interfaces;
using PawRescue.Domain.Entities;
using PawRescue.Domain.Enums;

namespace PawRescue.Application.Services;

public class ReportService : IReportService
{
    private static readonly string[] AllowedImageTypes = { "image/jpeg", "image/png", "image/webp", "image/heic" };
    private const long MaxImageBytes = 8 * 1024 * 1024; // 8 MB
    private const double NearbyNotifyRadiusKm = 5;

    private readonly IReportRepository _reports;
    private readonly IUnitOfWork _uow;
    private readonly IImageStorageService _storage;
    private readonly IPriorityStrategy _priority;
    private readonly INotificationService _notifications;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditLogger _audit;
    private readonly IAnimalHealthAnalyzer _healthAnalyzer;
    private readonly ILogger<ReportService> _logger;

    public ReportService(IReportRepository reports, IUnitOfWork uow, IImageStorageService storage,
                         IPriorityStrategy priority, INotificationService notifications,
                         ICurrentUserService currentUser, IAuditLogger audit,
                         IAnimalHealthAnalyzer healthAnalyzer, ILogger<ReportService> logger)
    {
        _reports = reports;
        _uow = uow;
        _storage = storage;
        _priority = priority;
        _notifications = notifications;
        _currentUser = currentUser;
        _audit = audit;
        _healthAnalyzer = healthAnalyzer;
        _logger = logger;
    }

    private Guid CurrentUserId => _currentUser.UserId ?? throw new UnauthorizedException("Not logged in.");

    public async Task<ReportDto> CreateAsync(CreateReportRequest request, IReadOnlyList<ImageUpload> images, CancellationToken ct = default)
    {
        if (images.Count == 0) throw new ValidationException("Please attach at least one photo of the animal.");
        if (images.Count > 5) throw new ValidationException("You can attach up to 5 photos.");
        foreach (var img in images) ValidateImage(img);

        var location = new Location(request.Latitude, request.Longitude, request.Address);
        var report = new AnimalReport(CurrentUserId, request.Title, request.Description ?? string.Empty,
                                      request.AnimalType, request.Condition, request.Severity, location);

        // Upload to cloud storage first; if the DB save fails we clean the uploads up.
        var uploaded = new List<StoredImage>();
        try
        {
            foreach (var img in images)
            {
                var stored = await _storage.UploadAsync(img, $"reports/{report.Id}", ct);
                uploaded.Add(stored);
                report.AddImage(new ReportImage(report.Id, stored.Url, stored.StorageKey, img.ContentType, img.Length, false));
            }

            report.SetPriority(_priority.Calculate(report));
            await _reports.AddAsync(report, ct);
            await _uow.SaveChangesAsync(ct);
        }
        catch
        {
            foreach (var s in uploaded)
            {
                try { await _storage.DeleteAsync(s.StorageKey, CancellationToken.None); }
                catch (Exception ex) { _logger.LogWarning(ex, "Failed to clean up image {Key}", s.StorageKey); }
            }
            throw;
        }

        await _audit.LogAsync("REPORT_CREATED", nameof(AnimalReport), report.Id.ToString(),
            new { report.Title, report.Severity }, ct);

        // Fire-and-forget would lose errors; we await but never fail the request because of it.
        try { await _notifications.NotifyNearbyVolunteersAsync(report, NearbyNotifyRadiusKm, ct); }
        catch (Exception ex) { _logger.LogError(ex, "Nearby notification failed for report {Id}", report.Id); }

        return await GetByIdAsync(report.Id, ct);
    }

    public async Task<ReportDto> UpdateAsync(Guid id, UpdateReportRequest request, CancellationToken ct = default)
    {
        var report = await LoadAsync(id, ct);
        EnsureOwnerOrAdmin(report);

        report.UpdateDetails(request.Title, request.Description ?? string.Empty, request.AnimalType, request.Condition, request.Severity);
        report.Location.Update(request.Latitude, request.Longitude, request.Address);
        report.SetPriority(_priority.Calculate(report));

        await _uow.SaveChangesAsync(ct);
        await _audit.LogAsync("REPORT_UPDATED", nameof(AnimalReport), id.ToString(), null, ct);
        return report.ToDto();
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var report = await LoadAsync(id, ct);
        EnsureOwnerOrAdmin(report);

        if (!_currentUser.IsInRole(RoleNames.Administrator) && report.Status != ReportStatus.Reported)
            throw new ValidationException("A report can only be deleted before a rescuer is assigned.");

        report.SoftDelete();
        await _uow.SaveChangesAsync(ct);
        await _audit.LogAsync("REPORT_DELETED", nameof(AnimalReport), id.ToString(), null, ct);
    }

    public async Task<ReportDto> GetByIdAsync(Guid id, CancellationToken ct = default)
        => (await LoadAsync(id, ct)).ToDto();

    public async Task<IReadOnlyList<ReportSummaryDto>> GetNearbyAsync(NearbyQuery q, CancellationToken ct = default)
    {
        var box = GeoCalculator.BoundingBox(q.Latitude, q.Longitude, q.RadiusKm);
        var candidates = await _reports.GetInBoundingBoxAsync(box.MinLat, box.MaxLat, box.MinLon, box.MaxLon,
            q.MinSeverity, q.Status, q.OpenOnly, Math.Clamp(q.Limit, 1, 500), ct);

        return candidates
            .Select(r => (Report: r, Distance: GeoCalculator.DistanceKm(q.Latitude, q.Longitude, r.Location.Latitude, r.Location.Longitude)))
            .Where(x => x.Distance <= q.RadiusKm) // the box is a square; trim the corners
            .OrderByDescending(x => x.Report.PriorityScore)
            .ThenBy(x => x.Distance)
            .Select(x => x.Report.ToSummary(x.Distance))
            .ToList();
    }

    public async Task<PagedResult<ReportSummaryDto>> SearchAsync(SearchQuery query, CancellationToken ct = default)
    {
        var page = Math.Max(1, query.Page);
        var size = Math.Clamp(query.PageSize, 1, 100);
        var (items, total) = await _reports.SearchAsync(query with { Page = page, PageSize = size }, ct);
        return new PagedResult<ReportSummaryDto>(items.Select(r => r.ToSummary()).ToList(), page, size, total);
    }

    public async Task<IReadOnlyList<ReportSummaryDto>> GetMyReportsAsync(CancellationToken ct = default)
        => (await _reports.GetByReporterAsync(CurrentUserId, ct)).Select(r => r.ToSummary()).ToList();

    /// <summary>FUTURE FEATURE — the analyzer currently throws FeatureNotImplementedException (HTTP 501).</summary>
    public async Task<HealthAnalysisResult> AnalyzeHealthAsync(Guid id, CancellationToken ct = default)
    {
        var report = await LoadAsync(id, ct);
        var imageUrl = report.Images.FirstOrDefault(i => i.IsPrimary)?.Url
                       ?? throw new ValidationException("Report has no image to analyze.");
        return await _healthAnalyzer.AnalyzeAsync(imageUrl, ct);
    }

    /// <summary>Home screen stats: total reports, animals helped (rescued or closed after rescue), open cases.</summary>
    public async Task<CommunityStatsDto> GetCommunityStatsAsync(CancellationToken ct = default)
    {
        var byStatus = await _reports.CountByStatusAsync(ct);
        return new CommunityStatsDto(
            AnimalsReported: await _reports.CountAsync(ct),
            AnimalsHelped: byStatus.GetValueOrDefault(ReportStatus.Rescued) + byStatus.GetValueOrDefault(ReportStatus.Closed),
            ActiveCases: await _reports.CountOpenAsync(null, ct));
    }

    // ---------- helpers ----------

    private async Task<AnimalReport> LoadAsync(Guid id, CancellationToken ct)
        => await _reports.GetDetailsAsync(id, ct) ?? throw new NotFoundException("Report", id);

    private void EnsureOwnerOrAdmin(AnimalReport report)
    {
        if (report.ReporterId != CurrentUserId && !_currentUser.IsInRole(RoleNames.Administrator))
            throw new ForbiddenException("Only the reporter or an administrator can modify this report.");
    }

    private static void ValidateImage(ImageUpload img)
    {
        if (img.Length <= 0) throw new ValidationException($"'{img.FileName}' is empty.");
        if (img.Length > MaxImageBytes) throw new ValidationException($"'{img.FileName}' is larger than 8 MB.");
        if (!AllowedImageTypes.Contains(img.ContentType.ToLowerInvariant()))
            throw new ValidationException($"'{img.FileName}' must be a JPEG, PNG, WEBP or HEIC image.");
    }
}
