using PawRescue.Application.Common;
using PawRescue.Application.Notifications;
using PawRescue.Application.Services;
using PawRescue.Domain.Common;
using PawRescue.Domain.Entities;
using PawRescue.Domain.Enums;
using Xunit;

namespace PawRescue.Tests;

public class AnimalReportTests
{
    private static AnimalReport NewReport(SeverityLevel severity = SeverityLevel.High, AnimalCondition condition = AnimalCondition.Injured)
        => new(Guid.NewGuid(), "Injured puppy", "Limping near the market", AnimalType.Dog, condition, severity,
               new Location(14.8136, 121.0453, "SJDM, Bulacan"));

    [Fact]
    public void New_report_starts_as_Reported()
        => Assert.Equal(ReportStatus.Reported, NewReport().Status);

    [Fact]
    public void Follows_the_full_rescue_workflow()
    {
        var r = NewReport();
        r.ChangeStatus(ReportStatus.UnderReview);
        r.ChangeStatus(ReportStatus.RescueAssigned);
        r.ChangeStatus(ReportStatus.InProgress);
        r.ChangeStatus(ReportStatus.Rescued);
        r.ChangeStatus(ReportStatus.Closed);
        Assert.Equal(ReportStatus.Closed, r.Status);
    }

    [Fact]
    public void Cannot_skip_from_Reported_to_Rescued()
    {
        var r = NewReport();
        Assert.Throws<DomainException>(() => r.ChangeStatus(ReportStatus.Rescued));
    }

    [Fact]
    public void Closed_report_cannot_be_edited()
    {
        var r = NewReport();
        r.ChangeStatus(ReportStatus.Closed);
        Assert.Throws<DomainException>(() =>
            r.UpdateDetails("x", "y", AnimalType.Cat, AnimalCondition.Sick, SeverityLevel.Low));
    }

    [Fact]
    public void First_image_becomes_primary_and_max_is_five()
    {
        var r = NewReport();
        for (var i = 0; i < 5; i++)
            r.AddImage(new ReportImage(r.Id, $"https://img/{i}.jpg", $"k{i}", "image/jpeg", 100, false));

        Assert.True(r.Images.First().IsPrimary);
        Assert.Throws<DomainException>(() =>
            r.AddImage(new ReportImage(r.Id, "https://img/6.jpg", "k6", "image/jpeg", 100, false)));
    }

    [Theory]
    [InlineData(91, 0)]
    [InlineData(0, 181)]
    public void Location_rejects_invalid_coordinates(double lat, double lng)
        => Assert.Throws<DomainException>(() => new Location(lat, lng));
}

public class PasswordResetTokenTests
{
    [Fact]
    public void Reset_token_rejects_wrong_hash()
    {
        var user = new User("Test User", "test@example.com", "old-hash", 1);
        user.SetPasswordResetToken("expected-hash", DateTime.UtcNow.AddMinutes(15));
        Assert.False(user.IsPasswordResetTokenValid("wrong-hash"));
    }

    [Fact]
    public void Reset_token_rejects_after_expiry()
    {
        var user = new User("Test User", "test@example.com", "old-hash", 1);
        user.SetPasswordResetToken("expected-hash", DateTime.UtcNow.AddMinutes(-1));
        Assert.False(user.IsPasswordResetTokenValid("expected-hash"));
    }
}

public class PriorityStrategyTests
{
    [Fact]
    public void Critical_injured_scores_higher_than_low_lost()
    {
        var strategy = new SeverityPriorityStrategy();
        var loc = new Location(14.8, 121.0);
        var critical = new AnimalReport(Guid.NewGuid(), "a", "", AnimalType.Dog, AnimalCondition.Injured, SeverityLevel.Critical, loc);
        var low = new AnimalReport(Guid.NewGuid(), "b", "", AnimalType.Cat, AnimalCondition.Lost, SeverityLevel.Low, loc);

        Assert.True(strategy.Calculate(critical) > strategy.Calculate(low));
        Assert.InRange(strategy.Calculate(critical), 0, 100);
    }
}

public class GeoCalculatorTests
{
    [Fact]
    public void Distance_between_SJDM_and_Quezon_City_is_about_15km()
    {
        var km = GeoCalculator.DistanceKm(14.8136, 121.0453, 14.6760, 121.0437);
        Assert.InRange(km, 14, 17);
    }

    [Fact]
    public void Bounding_box_contains_center()
    {
        var box = GeoCalculator.BoundingBox(14.8, 121.0, 5);
        Assert.True(box.MinLat < 14.8 && box.MaxLat > 14.8 && box.MinLon < 121.0 && box.MaxLon > 121.0);
    }
}

public class NotificationTemplateTests
{
    [Fact]
    public void Templates_are_polymorphic()
    {
        var id = Guid.NewGuid();
        NotificationTemplate[] templates =
        {
            new RescueAcceptedNotification(id, "Puppy", "Ana"),
            new StatusChangedNotification(id, "Puppy", ReportStatus.Rescued),
            new NewNearbyReportNotification(id, "Puppy", SeverityLevel.Critical, 1.2)
        };

        var entities = templates.Select(t => t.ToEntity(Guid.NewGuid())).ToList();

        Assert.Equal(new[] { NotificationType.RescueAccepted, NotificationType.StatusChanged, NotificationType.NewNearbyReport },
                     entities.Select(e => e.Type));
        Assert.All(entities, e => Assert.False(string.IsNullOrWhiteSpace(e.Message)));
    }
}
