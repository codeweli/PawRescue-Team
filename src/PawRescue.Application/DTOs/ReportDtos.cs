using System.ComponentModel.DataAnnotations;
using PawRescue.Domain.Enums;

namespace PawRescue.Application.DTOs;

/// <summary>Image bytes are passed as a stream so the Application layer stays framework-agnostic (no IFormFile).</summary>
public record ImageUpload(Stream Content, string FileName, string ContentType, long Length);

public record CreateReportRequest(
    [Required, MaxLength(120)] string Title,
    [MaxLength(2000)] string? Description,
    AnimalType AnimalType,
    AnimalCondition Condition,
    SeverityLevel Severity,
    [Range(-90, 90)] double Latitude,
    [Range(-180, 180)] double Longitude,
    [MaxLength(300)] string? Address);

public record UpdateReportRequest(
    [Required, MaxLength(120)] string Title,
    [MaxLength(2000)] string? Description,
    AnimalType AnimalType,
    AnimalCondition Condition,
    SeverityLevel Severity,
    [Range(-90, 90)] double Latitude,
    [Range(-180, 180)] double Longitude,
    [MaxLength(300)] string? Address);

public record ImageDto(Guid Id, string Url, bool IsPrimary);

public record LocationDto(double Latitude, double Longitude, string? Address, string? City);

public record AssignmentDto(
    Guid Id,
    Guid ReportId,
    string ReportTitle,
    Guid VolunteerId,
    string VolunteerName,
    AssignmentStatus Status,
    DateTime AssignedAt,
    DateTime? CompletedAt,
    string? Notes);

public record ReportDto(
    Guid Id,
    string Title,
    string Description,
    AnimalType AnimalType,
    AnimalCondition Condition,
    SeverityLevel Severity,
    ReportStatus Status,
    int PriorityScore,
    LocationDto Location,
    IReadOnlyList<ImageDto> Images,
    Guid ReporterId,
    string ReporterName,
    AssignmentDto? ActiveAssignment,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

/// <summary>Lightweight item used by lists and map pins.</summary>
public record ReportSummaryDto(
    Guid Id,
    string Title,
    AnimalType AnimalType,
    AnimalCondition Condition,
    SeverityLevel Severity,
    ReportStatus Status,
    int PriorityScore,
    double Latitude,
    double Longitude,
    string? Address,
    string? ThumbnailUrl,
    double? DistanceKm,
    DateTime CreatedAt);

public record NearbyQuery(
    [Range(-90, 90)] double Latitude,
    [Range(-180, 180)] double Longitude,
    [Range(0.1, 100)] double RadiusKm = 5,
    SeverityLevel? MinSeverity = null,
    ReportStatus? Status = null,
    bool OpenOnly = true,
    int Limit = 200);

public record SearchQuery(
    string? Keyword = null,
    AnimalType? AnimalType = null,
    SeverityLevel? Severity = null,
    ReportStatus? Status = null,
    int Page = 1,
    int PageSize = 20);

/// <summary>Numbers shown on the Home screen stats card.</summary>
public record CommunityStatsDto(int AnimalsReported, int AnimalsHelped, int ActiveCases);

public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)Math.Max(PageSize, 1));
}
