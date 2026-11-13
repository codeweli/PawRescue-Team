namespace PawRescue.Mobile.Models;

// Client-side copies of the API DTOs. Enums are sent as strings (JsonStringEnumConverter on both ends).

public enum ReportStatus { Reported, UnderReview, RescueAssigned, InProgress, Rescued, Closed }
public enum SeverityLevel { Low, Medium, High, Critical }
public enum AnimalType { Dog, Cat, Bird, Other }
public enum AnimalCondition { Starving, Injured, Abandoned, Sick, Lost, Other }
public enum NotificationType { RescueAccepted, StatusChanged, NewNearbyReport, System }
public enum AssignmentStatus { Active, Completed, Cancelled }

public static class Roles
{
    public const string User = "User";
    public const string Volunteer = "Volunteer";
    public const string Administrator = "Administrator";
}

// ---------- Auth ----------
public record LoginRequest(string Email, string Password, string? DeviceToken = null);
public record RegisterRequest(string FullName, string Email, string Password, string? PhoneNumber, bool RegisterAsVolunteer);
public record RefreshTokenRequest(string RefreshToken);
public record ForgotPasswordRequest(string Email);
public record ResetPasswordRequest(string Email, string Token, string NewPassword);

public record UserDto(Guid Id, string FullName, string Email, string? PhoneNumber, string? ProfileImageUrl,
                      string Role, bool IsActive, DateTime CreatedAt);

public record AuthResponse(string AccessToken, DateTime AccessTokenExpiresAt, string RefreshToken, UserDto User);

// ---------- Reports ----------
public record ImageDto(Guid Id, string Url, bool IsPrimary);
public record LocationDto(double Latitude, double Longitude, string? Address, string? City);

public record AssignmentDto(Guid Id, Guid ReportId, string ReportTitle, Guid VolunteerId, string VolunteerName,
                            AssignmentStatus Status, DateTime AssignedAt, DateTime? CompletedAt, string? Notes);

public record ReportDto(Guid Id, string Title, string Description, AnimalType AnimalType, AnimalCondition Condition,
                        SeverityLevel Severity, ReportStatus Status, int PriorityScore, LocationDto Location,
                        List<ImageDto> Images, Guid ReporterId, string ReporterName, AssignmentDto? ActiveAssignment,
                        DateTime CreatedAt, DateTime? UpdatedAt);

public record ReportSummaryDto(Guid Id, string Title, AnimalType AnimalType, AnimalCondition Condition,
                               SeverityLevel Severity, ReportStatus Status, int PriorityScore,
                               double Latitude, double Longitude, string? Address, string? ThumbnailUrl,
                               double? DistanceKm, DateTime CreatedAt)
{
    public string DistanceText => DistanceKm is null ? string.Empty : $"{DistanceKm:0.0} km away";
    public string AnimalEmoji => AnimalType switch
    {
        AnimalType.Dog => "🐶", AnimalType.Cat => "🐱", AnimalType.Bird => "🐦", _ => "🐾"
    };

    // ----- Display helpers used by the report cards (Home, Rescue Cases, Map preview) -----
    public bool IsOpen => Status is not (ReportStatus.Rescued or ReportStatus.Closed);
    public bool IsHighPriority => IsOpen && Severity >= SeverityLevel.High;
    public string BadgeText => IsHighPriority ? "! High Priority" : Status switch
    {
        ReportStatus.Rescued => "• Rescued",
        ReportStatus.Closed => "• Closed",
        ReportStatus.InProgress => "• In Progress",
        ReportStatus.RescueAssigned => "• Assigned",
        _ => "• Open"
    };
    public string LocationText => string.IsNullOrWhiteSpace(Address) ? "Pinned on map" : Address!;
    public NeedCategory Need => Needs.From(Condition, Status);
    public string NeedText => Needs.CardLabel(Condition, Status);
    public Color NeedColor => Needs.ColorOf(Need);
    public string Subtitle => $"{AnimalType} · {LocationText}";
}

/// <summary>
/// What kind of help an animal needs. Derived from Condition + Status, so no database change is needed.
/// Used for the colored dots on cards, the map pin colors and the map legend.
/// </summary>
public enum NeedCategory { Medical, Food, Shelter, Provided, Other }

public static class Needs
{
    public static NeedCategory From(AnimalCondition condition, ReportStatus status)
    {
        if (status is ReportStatus.Rescued or ReportStatus.Closed) return NeedCategory.Provided;
        return condition switch
        {
            AnimalCondition.Injured or AnimalCondition.Sick => NeedCategory.Medical,
            AnimalCondition.Starving => NeedCategory.Food,
            AnimalCondition.Abandoned or AnimalCondition.Lost => NeedCategory.Shelter,
            _ => NeedCategory.Other
        };
    }

    public static string CardLabel(AnimalCondition condition, ReportStatus status)
    {
        if (status is ReportStatus.Rescued or ReportStatus.Closed) return "Assistance Provided";
        return condition switch
        {
            AnimalCondition.Injured => "Medical Treatment",
            AnimalCondition.Sick => "Medical Treatment",
            AnimalCondition.Starving => "Food and Water",
            AnimalCondition.Abandoned => "Temporary Shelter",
            AnimalCondition.Lost => "Help Finding Home",
            _ => "Needs Assistance"
        };
    }

    public static string MapLabel(NeedCategory need) => need switch
    {
        NeedCategory.Medical => "Medical attention needed",
        NeedCategory.Food => "Food needed",
        NeedCategory.Shelter => "Shelter needed",
        NeedCategory.Provided => "Assistance provided",
        _ => "Needs assistance"
    };

    public static Color ColorOf(NeedCategory need) => need switch
    {
        NeedCategory.Medical => Color.FromArgb("#D7263D"),
        NeedCategory.Food => Color.FromArgb("#F2802C"),
        NeedCategory.Shelter => Color.FromArgb("#EFC21B"),
        NeedCategory.Provided => Color.FromArgb("#2E9E5B"),
        _ => Color.FromArgb("#8A8486")
    };

    /// <summary>Google Maps marker hue (0–360) that matches ColorOf.</summary>
    public static float HueOf(NeedCategory need) => need switch
    {
        NeedCategory.Medical => 355f,
        NeedCategory.Food => 28f,
        NeedCategory.Shelter => 50f,
        NeedCategory.Provided => 140f,
        _ => 210f
    };
}

public record CommunityStatsDto(int AnimalsReported, int AnimalsHelped, int ActiveCases);

public record UpdateReportRequest(string Title, string? Description, AnimalType AnimalType, AnimalCondition Condition,
                                  SeverityLevel Severity, double Latitude, double Longitude, string? Address);

public record UpdateStatusRequest(ReportStatus Status, string? Note);

public record PagedResult<T>(List<T> Items, int Page, int PageSize, int TotalCount, int TotalPages);

// ---------- Notifications / profile / admin ----------
public record NotificationDto(Guid Id, NotificationType Type, string Title, string Message, Guid? ReportId,
                              bool IsRead, DateTime CreatedAt)
{
    public string Icon => Type switch
    {
        NotificationType.RescueAccepted => "🤝",
        NotificationType.StatusChanged => "🔄",
        NotificationType.NewNearbyReport => "📍",
        _ => "🔔"
    };

    private bool IsGoodNews => Message.Contains("rescued", StringComparison.OrdinalIgnoreCase);

    /// <summary>Outline icon (Resources/Images) for the round badge on the left.</summary>
    public string IconImage => Type switch
    {
        NotificationType.RescueAccepted => "icon_person.png",
        NotificationType.StatusChanged when IsGoodNews => "icon_heart.png",
        NotificationType.StatusChanged => "icon_clock.png",
        NotificationType.NewNearbyReport => "icon_pin.png",
        _ => "icon_bell_small.png"
    };

    public Color IconBackground => Type switch
    {
        NotificationType.RescueAccepted => Color.FromArgb("#FCE8EA"),
        NotificationType.StatusChanged when IsGoodNews => Color.FromArgb("#E3F4EA"),
        NotificationType.StatusChanged => Color.FromArgb("#FFF0E0"),
        _ => Color.FromArgb("#F1EFEF")
    };

    public string TimeAgo => TimeText.Ago(CreatedAt);
}

public static class TimeText
{
    /// <summary>"12 min ago", "1 hr ago", "Yesterday", "Oct 22".</summary>
    public static string Ago(DateTime utc)
    {
        var local = DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();
        var span = DateTime.Now - local;
        if (span.TotalMinutes < 1) return "Just now";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes} min ago";
        if (span.TotalHours < 24 && local.Date == DateTime.Today) return $"{(int)span.TotalHours} hr ago";
        if (local.Date == DateTime.Today.AddDays(-1)) return "Yesterday";
        return local.ToString(local.Year == DateTime.Now.Year ? "MMM d" : "MMM d, yyyy");
    }
}

public record UserStatsDto(int ReportsSubmitted, int ReportsRescued, int RescuesCompleted, int ActiveRescues);
public record ProfileDto(UserDto User, UserStatsDto Stats);
public record UpdateProfileRequest(string FullName, string? PhoneNumber);
public record UpdateLocationRequest(double Latitude, double Longitude, string? DeviceToken);

public record DashboardStatsDto(int TotalUsers, int TotalVolunteers, int TotalReports, int OpenReports,
                                int RescuedReports, int CriticalOpenReports, int ReportsLast7Days,
                                Dictionary<string, int> ReportsByStatus, Dictionary<string, int> ReportsByAnimalType);

public record AuditLogDto(Guid Id, string? UserName, string Action, string EntityName, string? EntityId,
                          string? Details, DateTime CreatedAt);

public record ChangeRoleRequest(string Role);

public record HealthAnalysisResult(string PredictedCondition, double Confidence, string? Notes);

/// <summary>A photo chosen in the Report screen, before upload.</summary>
public class PhotoItem
{
    public required string FilePath { get; init; }
    public required string FileName { get; init; }
    public required string ContentType { get; init; }
    public ImageSource Preview => ImageSource.FromFile(FilePath);
}

/// <summary>A pin (single report or a cluster of reports) shown on the map.</summary>
public class MapPinItem
{
    public required Microsoft.Maui.Devices.Sensors.Location Location { get; init; }
    public required string Label { get; init; }
    public string Address { get; init; } = string.Empty;
    public Guid? ReportId { get; init; }       // null when this pin is a cluster
    public int Count { get; init; } = 1;
    public bool IsCluster => Count > 1;

    // Used by the colored marker and the bottom preview card on the Map screen
    public float Hue { get; init; } = 355f;
    public string Name { get; init; } = string.Empty;
    public string Subtitle { get; init; } = string.Empty;
    public string NeedText { get; init; } = string.Empty;
    public string ConditionText { get; init; } = string.Empty;
    public Color NeedColor { get; init; } = Colors.Gray;
    public string? ThumbnailUrl { get; init; }
}
