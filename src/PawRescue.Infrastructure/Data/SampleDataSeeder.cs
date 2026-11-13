using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PawRescue.Application.Interfaces;
using PawRescue.Application.Notifications;
using PawRescue.Application.Services;
using PawRescue.Domain.Entities;
using PawRescue.Domain.Enums;

namespace PawRescue.Infrastructure.Data;

/// <summary>
/// DEMO DATA ONLY. Adds sample animal reports (each with a real photo) in SAN PABLO CITY, LAGUNA
/// so the map, Home and Rescue Cases screens have something to show during testing/defense.
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

    // All samples are in SAN PABLO CITY, LAGUNA, near public landmarks (cathedral, old city hall,
    // Sampaloc Lake, public market, Brgy. San Marcos). Names/notes are made up.
    // EVERY sample has a photo in src/PawRescue.Api/wwwroot/samples/<name>.jpg — a sample whose photo
    // file is missing is simply not created (no picture-less reports).
    private static readonly Sample[] Samples =
    {
        // ---- dogs ----
        new("Puti", AnimalType.Dog, AnimalCondition.Sick, SeverityLevel.Medium, ReportStatus.Rescued,
            14.0731, 121.3268, "Near the Old City Hall", DemoCity, "Laguna",
            "Friendly cream-colored aspin with a skin infection. Treated by a volunteer vet and now doing great."),
        new("Lucky", AnimalType.Dog, AnimalCondition.Lost, SeverityLevel.Low, ReportStatus.Reported,
            14.0712, 121.3281, "Near San Pablo Cathedral", DemoCity, "Laguna",
            "Cream dog with a purple collar resting under a parked motorcycle. Looks cared for — probably lost."),
        new("Brownie", AnimalType.Dog, AnimalCondition.Starving, SeverityLevel.High, ReportStatus.RescueAssigned,
            14.0657, 121.3224, "San Pablo City Public Market, parking area", DemoCity, "Laguna",
            "Two thin brown aspins staying together in the parking area, asking vendors for scraps."),
        new("Bunso", AnimalType.Dog, AnimalCondition.Abandoned, SeverityLevel.High, ReportStatus.Reported,
            14.1040, 121.3015, "Brgy. San Marcos sidewalk", DemoCity, "Laguna",
            "Tiny puppy alone by the curb, crying. No mother dog nearby. Very young."),

        // ---- cats (newest, so they show first) ----
        new("Mingming", AnimalType.Cat, AnimalCondition.Sick, SeverityLevel.Medium, ReportStatus.InProgress,
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
    /// Makes sure the demo data matches the list above:
    ///   1. removes demo reports that are no longer in the list (old samples, samples without photos),
    ///   2. adds any listed sample that is missing AND has a photo file,
    ///   3. attaches the photo to any sample report that has none yet.
    /// Safe to run on every start. Reports made by real users are never touched.
    /// </summary>
    public static async Task SeedAsync(AppDbContext db, IPasswordHasher hasher, string webRootPath, string publicBaseUrl, ILogger logger)
    {
        var photoFolder = Path.Combine(webRootPath, "samples");

        await RemoveOutdatedDemoDataAsync(db, logger);

        var reporter = await GetOrCreateUserAsync(db, hasher, "Juan Dela Cruz", ReporterEmail, RoleIds.User);
        var volunteer = await GetOrCreateUserAsync(db, hasher, "Maria Santos", VolunteerEmail, RoleIds.Volunteer);
        volunteer.UpdateLastKnownLocation(14.0698, 121.3265); // San Pablo Cathedral, so "nearby" alerts make sense
        await db.SaveChangesAsync();

        var existingTitles = await db.AnimalReports.IgnoreQueryFilters()
            .Where(r => r.ReporterId == reporter.Id)
            .Select(r => r.Title)
            .ToListAsync();

        var priority = new SeverityPriorityStrategy();
        var created = 0;

        foreach (var s in Samples)
        {
            if (existingTitles.Contains(s.Name)) continue;
            var photo = FindPhoto(photoFolder, s.Name);
            if (photo is null)
            {
                logger.LogWarning("Sample {Name} skipped: no photo at wwwroot/samples/{File}.jpg", s.Name, s.Name.ToLowerInvariant());
                continue;
            }

            var location = new Location(s.Lat, s.Lng, s.Address, null, s.City, s.Province);
            var report = new AnimalReport(reporter.Id, s.Name, s.Notes, s.Type, s.Condition, s.Severity, location);
            AddPhoto(db, report, photo, publicBaseUrl);

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
                db.Notifications.Add(new NewNearbyReportNotification(report.Id, report.Title, report.Severity, 0.8).ToEntity(volunteer.Id));

            await db.SaveChangesAsync();   // one at a time → the newest (cats) end up first in lists
            created++;
        }

        // Older sample reports that were created before their photo existed
        var withoutPhoto = await db.AnimalReports.Include(r => r.Images)
            .Where(r => r.ReporterId == reporter.Id && !r.Images.Any())
            .ToListAsync();
        foreach (var report in withoutPhoto)
        {
            var photo = FindPhoto(photoFolder, report.Title);
            if (photo is not null) AddPhoto(db, report, photo, publicBaseUrl);
        }
        await db.SaveChangesAsync();

        if (created > 0) logger.LogInformation("Seeded {Count} sample reports in {City}", created, DemoCity);
    }

    private static string? FindPhoto(string folder, string name) =>
        new[] { ".jpg", ".jpeg", ".png", ".webp" }
            .Select(ext => Path.Combine(folder, name.ToLowerInvariant() + ext))
            .FirstOrDefault(File.Exists);

    private static void AddPhoto(AppDbContext db, AnimalReport report, string file, string publicBaseUrl)
    {
        var fileName = Path.GetFileName(file);
        var contentType = Path.GetExtension(file).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "image/jpeg"
        };
        var image = new ReportImage(report.Id, $"{publicBaseUrl.TrimEnd('/')}/samples/{fileName}",
                                    $"samples/{fileName}", contentType, new FileInfo(file).Length, true);
        report.AddImage(image);
        // The image already has an Id (Guid), so EF could think it's an EXISTING row and try an UPDATE.
        // Mark it as new explicitly so EF does an INSERT.
        db.Images.Add(image);
    }

    /// <summary>Deletes demo reports that are not in the current sample list (old sets, picture-less samples).</summary>
    private static async Task RemoveOutdatedDemoDataAsync(AppDbContext db, ILogger logger)
    {
        var keep = Samples.Select(s => (s.Name, s.Address)).ToHashSet();
        var demo = await db.AnimalReports.IgnoreQueryFilters()
            .Include(r => r.Location)
            .Where(r => r.Reporter.Email == ReporterEmail)
            .ToListAsync();
        var old = demo.Where(r => !keep.Contains((r.Title, r.Location.Address ?? string.Empty))).ToList();
        if (old.Count == 0) return;

        var ids = old.Select(r => r.Id).ToList();

        // Their notifications, assignments, then the reports (images cascade), then their locations.
        db.Notifications.RemoveRange(await db.Notifications.Where(n => n.ReportId != null && ids.Contains(n.ReportId.Value)).ToListAsync());
        db.RescueAssignments.RemoveRange(await db.RescueAssignments.Where(a => ids.Contains(a.ReportId)).ToListAsync());
        db.AnimalReports.RemoveRange(old);
        await db.SaveChangesAsync();

        db.Locations.RemoveRange(old.Select(r => r.Location));
        await db.SaveChangesAsync();

        logger.LogInformation("Removed {Count} outdated demo reports", old.Count);
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
