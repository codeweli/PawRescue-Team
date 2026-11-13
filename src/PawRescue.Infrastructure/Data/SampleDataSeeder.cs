using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PawRescue.Application.Interfaces;
using PawRescue.Application.Notifications;
using PawRescue.Application.Services;
using PawRescue.Domain.Entities;
using PawRescue.Domain.Enums;

namespace PawRescue.Infrastructure.Data;

/// <summary>
/// DEMO DATA ONLY. Adds sample animal reports in SAN PABLO CITY, LAGUNA so the map, Home and
/// Rescue Cases screens have something to show during testing/defense.
/// Turn it off with  "Seed": { "SampleData": false }  in appsettings.json.
///
/// Demo accounts (password for both: Demo@12345):
///   reporter@pawrescue.local  – a normal user who "made" the reports
///   volunteer@pawrescue.local – a volunteer handling some of the rescues
/// </summary>
public static class SampleDataSeeder
{
    public const string DemoPassword = "Demo@12345";
    public const string ReporterEmail = "reporter@pawrescue.local";
    public const string VolunteerEmail = "volunteer@pawrescue.local";

    private record Sample(string Name, AnimalType Type, AnimalCondition Condition, SeverityLevel Severity,
                          ReportStatus Status, double Lat, double Lng, string Address, string City, string Province,
                          string Notes);

    /// <summary>All demo reports are in this city.</summary>
    public const string DemoCity = "San Pablo City";

    // All samples are in SAN PABLO CITY, LAGUNA. Coordinates are near public landmarks
    // (cathedral, old city hall, Sampaloc Lake, public market, Brgy. San Marcos). Names/notes are made up.
    // The 4 cats with photos (Kuting, Luna, Bella, Mingming) are added LAST so they are the newest
    // and appear first in lists. Their photos are in src/PawRescue.Api/wwwroot/samples/.
    private static readonly Sample[] Samples =
    {
        new("Brownie", AnimalType.Dog, AnimalCondition.Starving, SeverityLevel.Medium, ReportStatus.Reported,
            14.1040, 121.3015, "Brgy. San Marcos road", DemoCity, "Laguna",
            "Very thin brown aspin sleeping by the roadside waiting shed. Friendly and eats when given food."),
        new("Max", AnimalType.Dog, AnimalCondition.Abandoned, SeverityLevel.Medium, ReportStatus.Rescued,
            14.0731, 121.3268, "Near the Old City Hall", DemoCity, "Laguna",
            "Was tied to a post and left behind. Now safe with a foster family."),
        new("Bantay", AnimalType.Dog, AnimalCondition.Injured, SeverityLevel.Critical, ReportStatus.RescueAssigned,
            14.0768, 121.3236, "Sampaloc Lake road, west side", DemoCity, "Laguna",
            "Hit by a tricycle and can't stand on its back legs. Needs a vet urgently."),
        new("Choco", AnimalType.Dog, AnimalCondition.Lost, SeverityLevel.Low, ReportStatus.Reported,
            14.0712, 121.3281, "Near San Pablo Cathedral", DemoCity, "Laguna",
            "Brown poodle mix with a blue collar. Looks well cared for, probably lost."),
        new("Snow", AnimalType.Cat, AnimalCondition.Sick, SeverityLevel.High, ReportStatus.InProgress,
            14.0657, 121.3224, "San Pablo City Public Market, back entrance", DemoCity, "Laguna",
            "White cat with runny eyes, sneezing a lot. Hiding between the stalls."),

        // ---- The 4 sample cats with photos ----
        new("Mingming", AnimalType.Cat, AnimalCondition.Sick, SeverityLevel.Medium, ReportStatus.Reported,
            14.0739, 121.3253, "Old City Hall walkway", DemoCity, "Laguna",
            "Tabby cat lying on the walkway most of the day, not moving much. Might be sick."),
        new("Bella", AnimalType.Cat, AnimalCondition.Starving, SeverityLevel.Medium, ReportStatus.Reported,
            14.0744, 121.3291, "Sampaloc Lake park, near the plant boxes", DemoCity, "Laguna",
            "Calico cat that comes out of the bushes asking for food from people at the lake."),
        new("Luna", AnimalType.Cat, AnimalCondition.Starving, SeverityLevel.High, ReportStatus.Reported,
            14.0666, 121.3238, "San Pablo City Public Market, fish section drain", DemoCity, "Laguna",
            "Very thin tabby eating scraps from the drain. Looks weak and dehydrated."),
        new("Kuting", AnimalType.Cat, AnimalCondition.Abandoned, SeverityLevel.High, ReportStatus.Reported,
            14.0695, 121.3261, "Motorcycle parking beside San Pablo Cathedral", DemoCity, "Laguna",
            "Small kitten alone under the parked motorcycles. No mother cat around. Could get run over.")
    };

    /// <summary>
    /// Gives sample reports a photo when one exists in  src/PawRescue.Api/wwwroot/samples/
    /// named after the animal: luna.jpg, brownie.jpg, max.jpg, bella.jpg (.jpg/.jpeg/.png/.webp).
    /// Runs on every start, so you can add photos later without resetting the database.
    /// </summary>
    public static async Task AttachSamplePhotosAsync(AppDbContext db, string webRootPath, string publicBaseUrl, ILogger logger)
    {
        var folder = Path.Combine(webRootPath, "samples");
        if (!Directory.Exists(folder)) return;

        var demoReports = await db.AnimalReports
            .Include(r => r.Images)
            .Where(r => r.Reporter.Email == ReporterEmail && !r.Images.Any())
            .ToListAsync();

        var added = 0;
        foreach (var report in demoReports)
        {
            var file = new[] { ".jpg", ".jpeg", ".png", ".webp" }
                .Select(ext => Path.Combine(folder, report.Title.ToLowerInvariant() + ext))
                .FirstOrDefault(File.Exists);
            if (file is null) continue;

            var fileName = Path.GetFileName(file);
            var contentType = Path.GetExtension(file).ToLowerInvariant() switch
            {
                ".png" => "image/png",
                ".webp" => "image/webp",
                _ => "image/jpeg"
            };
            report.AddImage(new ReportImage(report.Id, $"{publicBaseUrl.TrimEnd('/')}/samples/{fileName}",
                                            $"samples/{fileName}", contentType, new FileInfo(file).Length, true));
            added++;
        }

        if (added > 0)
        {
            await db.SaveChangesAsync();
            logger.LogInformation("Attached {Count} sample photo(s)", added);
        }
    }

    public static async Task SeedAsync(AppDbContext db, IPasswordHasher hasher, ILogger logger)
    {
        // Old demo data from an earlier version (outside San Pablo City)? Remove it and seed again.
        await RemoveOutdatedDemoDataAsync(db, logger);

        // Only seed once: skip if the demo reporter already has reports. Your own reports are never touched.
        if (await db.AnimalReports.IgnoreQueryFilters().AnyAsync(r => r.Reporter.Email == ReporterEmail)) return;

        var reporter = await GetOrCreateUserAsync(db, hasher, "Juan Dela Cruz", ReporterEmail, RoleIds.User);
        var volunteer = await GetOrCreateUserAsync(db, hasher, "Maria Santos", VolunteerEmail, RoleIds.Volunteer);
        volunteer.UpdateLastKnownLocation(14.0698, 121.3265); // San Pablo Cathedral, so "nearby" alerts make sense

        var priority = new SeverityPriorityStrategy();

        foreach (var s in Samples)
        {
            var location = new Location(s.Lat, s.Lng, s.Address, null, s.City, s.Province);
            var report = new AnimalReport(reporter.Id, s.Name, s.Notes, s.Type, s.Condition, s.Severity, location);

            // Walk the real status workflow so the data follows the same rules as the app.
            foreach (var next in PathTo(s.Status))
                report.ChangeStatus(next);

            if (s.Status is ReportStatus.RescueAssigned or ReportStatus.InProgress or ReportStatus.Rescued)
            {
                var assignment = new RescueAssignment(report.Id, volunteer.Id);
                assignment.AddNote(s.Status == ReportStatus.Rescued ? "Brought to the vet. Recovering well." : "On the way.");
                if (s.Status == ReportStatus.Rescued) assignment.Complete();
                db.RescueAssignments.Add(assignment);

                db.Notifications.Add(new RescueAcceptedNotification(report.Id, report.Title, volunteer.FullName).ToEntity(reporter.Id));
                if (s.Status != ReportStatus.RescueAssigned)
                    db.Notifications.Add(new StatusChangedNotification(report.Id, report.Title, s.Status).ToEntity(reporter.Id));
            }

            report.SetPriority(priority.Calculate(report));
            db.AnimalReports.Add(report);

            if (report.IsOpen && report.Severity >= SeverityLevel.High)
                db.Notifications.Add(new NewNearbyReportNotification(report.Id, report.Title, report.Severity, 1.2).ToEntity(volunteer.Id));
        }

        await db.SaveChangesAsync();
        logger.LogInformation("Seeded {Count} sample reports across the Philippines", Samples.Length);
    }

    /// <summary>Deletes demo reports that are not in San Pablo City (from the older Philippines-wide sample set).</summary>
    private static async Task RemoveOutdatedDemoDataAsync(AppDbContext db, ILogger logger)
    {
        var old = await db.AnimalReports.IgnoreQueryFilters()
            .Include(r => r.Location)
            .Where(r => r.Reporter.Email == ReporterEmail && r.Location.City != DemoCity)
            .ToListAsync();
        if (old.Count == 0) return;

        var ids = old.Select(r => r.Id).ToList();
        var demoUserIds = await db.Users.Where(u => u.Email == ReporterEmail || u.Email == VolunteerEmail)
                                        .Select(u => u.Id).ToListAsync();

        // Notifications of the demo accounts, then the reports (images + assignments cascade), then their locations.
        db.Notifications.RemoveRange(await db.Notifications.Where(n => demoUserIds.Contains(n.UserId)).ToListAsync());
        db.RescueAssignments.RemoveRange(await db.RescueAssignments.Where(a => ids.Contains(a.ReportId)).ToListAsync());
        db.AnimalReports.RemoveRange(old);
        await db.SaveChangesAsync();

        db.Locations.RemoveRange(old.Select(r => r.Location));
        await db.SaveChangesAsync();

        logger.LogInformation("Removed {Count} outdated demo reports (outside {City})", old.Count, DemoCity);
    }

    private static IEnumerable<ReportStatus> PathTo(ReportStatus target) => target switch
    {
        ReportStatus.UnderReview => new[] { ReportStatus.UnderReview },
        ReportStatus.RescueAssigned => new[] { ReportStatus.RescueAssigned },
        ReportStatus.InProgress => new[] { ReportStatus.RescueAssigned, ReportStatus.InProgress },
        ReportStatus.Rescued => new[] { ReportStatus.RescueAssigned, ReportStatus.InProgress, ReportStatus.Rescued },
        ReportStatus.Closed => new[] { ReportStatus.Closed },
        _ => Array.Empty<ReportStatus>()
    };

    private static async Task<User> GetOrCreateUserAsync(AppDbContext db, IPasswordHasher hasher, string name, string email, int roleId)
    {
        var existing = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (existing is not null) return existing;

        var user = new User(name, email, hasher.Hash(DemoPassword), roleId);
        db.Users.Add(user);
        return user;
    }
}
