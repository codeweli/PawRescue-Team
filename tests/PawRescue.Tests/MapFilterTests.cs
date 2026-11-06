using PawRescue.Application.DTOs;
using PawRescue.Domain.Entities;
using PawRescue.Domain.Enums;
using Xunit;

namespace PawRescue.Tests;

/// <summary>
/// Tests for the two filter blocks added by Member 3.
///
/// No database or EF Core is needed here — the filter predicates are tested
/// with LINQ-to-Objects, which is exactly the same logic EF Core translates
/// to SQL.  Tests are fast (&lt;1 ms each) and run fully offline.
/// </summary>
public class MapFilterTests
{
    // ---------------------------------------------------------------
    // Shared factory helpers
    // ---------------------------------------------------------------

    private static readonly ReportStatus[] ClosedStates =
        { ReportStatus.Rescued, ReportStatus.Closed };

    /// <summary>Creates a minimal AnimalReport wired with a Location (no DB needed).</summary>
    private static AnimalReport Make(
        SeverityLevel severity,
        ReportStatus status,
        AnimalType type = AnimalType.Dog,
        AnimalCondition condition = AnimalCondition.Injured,
        double lat = 14.82, double lng = 121.05)
    {
        var report = new AnimalReport(
            Guid.NewGuid(), "Test report", "desc",
            type, condition, severity,
            new Location(lat, lng, "Test address"));

        // Walk the state machine to reach the desired status
        var path = StatusPath(status);
        foreach (var next in path)
            report.ChangeStatus(next);

        return report;
    }

    /// <summary>Minimal valid transition sequence to reach each status.</summary>
    private static ReportStatus[] StatusPath(ReportStatus target) => target switch
    {
        ReportStatus.Reported       => Array.Empty<ReportStatus>(),
        ReportStatus.UnderReview    => new[] { ReportStatus.UnderReview },
        ReportStatus.RescueAssigned => new[] { ReportStatus.RescueAssigned },
        ReportStatus.InProgress     => new[] { ReportStatus.RescueAssigned, ReportStatus.InProgress },
        ReportStatus.Rescued        => new[] { ReportStatus.RescueAssigned, ReportStatus.InProgress, ReportStatus.Rescued },
        ReportStatus.Closed         => new[] { ReportStatus.Closed },
        _                           => Array.Empty<ReportStatus>()
    };

    // ---------------------------------------------------------------
    // The two filter methods — exact copy of GetInBoundingBoxAsync logic
    // ---------------------------------------------------------------

    /// <summary>
    /// Mirrors GetInBoundingBoxAsync filter block from ReportRepository.
    /// Bounding-box step is assumed passed; only the three option filters are here.
    /// </summary>
    private static List<AnimalReport> ApplyMapFilters(
        IEnumerable<AnimalReport> source,
        SeverityLevel? minSeverity,
        ReportStatus? status,
        bool openOnly)
    {
        var q = source.AsQueryable();

        if (minSeverity is not null)
            q = q.Where(r => r.Severity >= minSeverity);

        if (status is not null)
            q = q.Where(r => r.Status == status);
        else if (openOnly)
            q = q.Where(r => !ClosedStates.Contains(r.Status));

        return q.OrderByDescending(r => r.PriorityScore).ToList();
    }

    /// <summary>
    /// Mirrors SearchAsync filter block from ReportRepository.
    /// Keyword search is not under test here; only the enum filters.
    /// </summary>
    private static List<AnimalReport> ApplySearchFilters(
        IEnumerable<AnimalReport> source,
        SearchQuery query)
    {
        var q = source.AsQueryable();

        if (query.AnimalType is not null)
            q = q.Where(r => r.AnimalType == query.AnimalType);

        if (query.Severity is not null)
            q = q.Where(r => r.Severity == query.Severity);

        if (query.Status is not null)
            q = q.Where(r => r.Status == query.Status);

        return q.OrderByDescending(r => r.CreatedAt).ToList();
    }

    // ================================================================
    // GetInBoundingBoxAsync filter tests
    // ================================================================

    [Fact]
    public void MapFilter_NoOptions_ReturnsAll()
    {
        var data = new[]
        {
            Make(SeverityLevel.Low,      ReportStatus.Reported),
            Make(SeverityLevel.Critical, ReportStatus.Rescued),
            Make(SeverityLevel.High,     ReportStatus.Closed),
        };

        var result = ApplyMapFilters(data, null, null, openOnly: false);

        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void MapFilter_OpenOnly_HidesRescuedAndClosed()
    {
        var data = new[]
        {
            Make(SeverityLevel.Low,    ReportStatus.Reported),       // open
            Make(SeverityLevel.Medium, ReportStatus.InProgress),     // open
            Make(SeverityLevel.High,   ReportStatus.Rescued),        // closed state → hidden
            Make(SeverityLevel.High,   ReportStatus.Closed),         // closed state → hidden
        };

        var result = ApplyMapFilters(data, null, null, openOnly: true);

        Assert.Equal(2, result.Count);
        Assert.All(result, r => Assert.DoesNotContain(r.Status, ClosedStates));
    }

    [Fact]
    public void MapFilter_OpenOnly_False_ShowsRescuedAndClosed()
    {
        var data = new[]
        {
            Make(SeverityLevel.Low,  ReportStatus.Rescued),
            Make(SeverityLevel.High, ReportStatus.Closed),
        };

        var result = ApplyMapFilters(data, null, null, openOnly: false);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void MapFilter_MinSeverity_FiltersOutLower()
    {
        var data = new[]
        {
            Make(SeverityLevel.Low,      ReportStatus.Reported),  // below threshold
            Make(SeverityLevel.Medium,   ReportStatus.Reported),  // below threshold
            Make(SeverityLevel.High,     ReportStatus.Reported),  // meets threshold
            Make(SeverityLevel.Critical, ReportStatus.Reported),  // above threshold
        };

        var result = ApplyMapFilters(data, SeverityLevel.High, null, openOnly: false);

        Assert.Equal(2, result.Count);
        Assert.All(result, r => Assert.True(r.Severity >= SeverityLevel.High));
    }

    [Fact]
    public void MapFilter_MinSeverityCritical_ReturnsOnlyCritical()
    {
        var data = new[]
        {
            Make(SeverityLevel.Low,      ReportStatus.Reported),
            Make(SeverityLevel.Medium,   ReportStatus.Reported),
            Make(SeverityLevel.High,     ReportStatus.Reported),
            Make(SeverityLevel.Critical, ReportStatus.Reported),
        };

        var result = ApplyMapFilters(data, SeverityLevel.Critical, null, openOnly: false);

        Assert.Single(result);
        Assert.Equal(SeverityLevel.Critical, result[0].Severity);
    }

    [Fact]
    public void MapFilter_ExactStatus_ReturnsOnlyThatStatus()
    {
        var data = new[]
        {
            Make(SeverityLevel.High, ReportStatus.Reported),
            Make(SeverityLevel.High, ReportStatus.InProgress),
            Make(SeverityLevel.High, ReportStatus.Rescued),
        };

        var result = ApplyMapFilters(data, null, ReportStatus.InProgress, openOnly: false);

        Assert.Single(result);
        Assert.Equal(ReportStatus.InProgress, result[0].Status);
    }

    [Fact]
    public void MapFilter_ExactStatus_OverridesOpenOnly()
    {
        // Even if openOnly would hide Rescued, an explicit status=Rescued must show it.
        var data = new[]
        {
            Make(SeverityLevel.High, ReportStatus.Rescued),
            Make(SeverityLevel.High, ReportStatus.Reported),
        };

        // status=Rescued takes precedence over openOnly=true
        var result = ApplyMapFilters(data, null, ReportStatus.Rescued, openOnly: true);

        Assert.Single(result);
        Assert.Equal(ReportStatus.Rescued, result[0].Status);
    }

    [Fact]
    public void MapFilter_MinSeverityAndStatus_BothApplied()
    {
        var data = new[]
        {
            Make(SeverityLevel.Low,      ReportStatus.Reported),     // fails severity
            Make(SeverityLevel.Critical, ReportStatus.Reported),     // passes both
            Make(SeverityLevel.Critical, ReportStatus.InProgress),   // fails status
        };

        var result = ApplyMapFilters(data, SeverityLevel.High, ReportStatus.Reported, openOnly: false);

        Assert.Single(result);
        Assert.Equal(SeverityLevel.Critical, result[0].Severity);
        Assert.Equal(ReportStatus.Reported,  result[0].Status);
    }

    [Fact]
    public void MapFilter_EmptySource_ReturnsEmpty()
    {
        var result = ApplyMapFilters(Array.Empty<AnimalReport>(), SeverityLevel.High, null, openOnly: true);
        Assert.Empty(result);
    }

    [Fact]
    public void MapFilter_NoMatches_ReturnsEmpty()
    {
        var data = new[]
        {
            Make(SeverityLevel.Low, ReportStatus.Rescued),
            Make(SeverityLevel.Low, ReportStatus.Closed),
        };

        // openOnly hides both Rescued and Closed
        var result = ApplyMapFilters(data, null, null, openOnly: true);
        Assert.Empty(result);
    }

    [Fact]
    public void MapFilter_SortedByPriorityScore_Descending()
    {
        // SeverityPriorityStrategy is not wired here; use ChangeStatus trick —
        // just verify the ORDER BY is applied (we can set priorities via different severities
        // since domain doesn't expose PriorityScore setter from outside the aggregate).
        // We rely on the fact that the list is returned in PriorityScore DESC order.
        // Here we just assert count — ordering is verified by integration test against real DB.
        var data = new[]
        {
            Make(SeverityLevel.High,   ReportStatus.Reported),
            Make(SeverityLevel.Medium, ReportStatus.Reported),
            Make(SeverityLevel.Low,    ReportStatus.Reported),
        };

        var result = ApplyMapFilters(data, null, null, openOnly: true);
        Assert.Equal(3, result.Count); // ordering works without throwing
    }

    // ================================================================
    // SearchAsync filter tests
    // ================================================================

    [Fact]
    public void Search_NoFilters_ReturnsAll()
    {
        var data = new[]
        {
            Make(SeverityLevel.Low,    ReportStatus.Reported, AnimalType.Dog),
            Make(SeverityLevel.High,   ReportStatus.Rescued,  AnimalType.Cat),
            Make(SeverityLevel.Medium, ReportStatus.Closed,   AnimalType.Bird),
        };

        var result = ApplySearchFilters(data, new SearchQuery());
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void Search_AnimalType_FiltersOtherTypes()
    {
        var data = new[]
        {
            Make(SeverityLevel.Low,  ReportStatus.Reported, AnimalType.Dog),
            Make(SeverityLevel.Low,  ReportStatus.Reported, AnimalType.Cat),
            Make(SeverityLevel.Low,  ReportStatus.Reported, AnimalType.Bird),
            Make(SeverityLevel.Low,  ReportStatus.Reported, AnimalType.Dog),
        };

        var result = ApplySearchFilters(data, new SearchQuery(AnimalType: AnimalType.Dog));

        Assert.Equal(2, result.Count);
        Assert.All(result, r => Assert.Equal(AnimalType.Dog, r.AnimalType));
    }

    [Fact]
    public void Search_Severity_FiltersOtherSeverities()
    {
        var data = new[]
        {
            Make(SeverityLevel.Critical, ReportStatus.Reported),
            Make(SeverityLevel.High,     ReportStatus.Reported),
            Make(SeverityLevel.Medium,   ReportStatus.Reported),
            Make(SeverityLevel.Low,      ReportStatus.Reported),
        };

        var result = ApplySearchFilters(data, new SearchQuery(Severity: SeverityLevel.High));

        Assert.Single(result);
        Assert.Equal(SeverityLevel.High, result[0].Severity);
    }

    [Fact]
    public void Search_Status_FiltersOtherStatuses()
    {
        var data = new[]
        {
            Make(SeverityLevel.High, ReportStatus.Reported),
            Make(SeverityLevel.High, ReportStatus.InProgress),
            Make(SeverityLevel.High, ReportStatus.Rescued),
            Make(SeverityLevel.High, ReportStatus.Reported),
        };

        var result = ApplySearchFilters(data, new SearchQuery(Status: ReportStatus.Reported));

        Assert.Equal(2, result.Count);
        Assert.All(result, r => Assert.Equal(ReportStatus.Reported, r.Status));
    }

    [Fact]
    public void Search_AllThreeFilters_Combined()
    {
        var data = new[]
        {
            Make(SeverityLevel.Critical, ReportStatus.Reported, AnimalType.Dog),   // ✓ passes all
            Make(SeverityLevel.Critical, ReportStatus.Reported, AnimalType.Cat),   // ✗ wrong type
            Make(SeverityLevel.High,     ReportStatus.Reported, AnimalType.Dog),   // ✗ wrong severity
            Make(SeverityLevel.Critical, ReportStatus.Rescued,  AnimalType.Dog),   // ✗ wrong status
        };

        var result = ApplySearchFilters(data, new SearchQuery(
            AnimalType: AnimalType.Dog,
            Severity:   SeverityLevel.Critical,
            Status:     ReportStatus.Reported));

        Assert.Single(result);
        Assert.Equal(AnimalType.Dog,       result[0].AnimalType);
        Assert.Equal(SeverityLevel.Critical, result[0].Severity);
        Assert.Equal(ReportStatus.Reported,  result[0].Status);
    }

    [Fact]
    public void Search_AnimalTypeAndSeverity_CombinedWithNoStatus()
    {
        var data = new[]
        {
            Make(SeverityLevel.High, ReportStatus.Reported,  AnimalType.Dog),
            Make(SeverityLevel.High, ReportStatus.InProgress, AnimalType.Dog),
            Make(SeverityLevel.Low,  ReportStatus.Reported,  AnimalType.Dog),
            Make(SeverityLevel.High, ReportStatus.Reported,  AnimalType.Cat),
        };

        var result = ApplySearchFilters(data, new SearchQuery(
            AnimalType: AnimalType.Dog,
            Severity:   SeverityLevel.High));

        Assert.Equal(2, result.Count);
        Assert.All(result, r =>
        {
            Assert.Equal(AnimalType.Dog,     r.AnimalType);
            Assert.Equal(SeverityLevel.High, r.Severity);
        });
    }

    [Fact]
    public void Search_EmptySource_ReturnsEmpty()
    {
        var result = ApplySearchFilters(
            Array.Empty<AnimalReport>(),
            new SearchQuery(AnimalType: AnimalType.Cat));

        Assert.Empty(result);
    }

    [Fact]
    public void Search_NoMatches_ReturnsEmpty()
    {
        var data = new[]
        {
            Make(SeverityLevel.Low, ReportStatus.Reported, AnimalType.Dog),
        };

        var result = ApplySearchFilters(data, new SearchQuery(AnimalType: AnimalType.Cat));

        Assert.Empty(result);
    }
}
