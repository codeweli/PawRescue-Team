using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PawRescue.Application.DTOs;
using PawRescue.Application.Interfaces;
using PawRescue.Domain.Enums;

namespace PawRescue.Api.Controllers;

/// <summary>multipart/form-data body for creating a report (fields + up to 5 photos).</summary>
public class CreateReportForm
{
    [Required, MaxLength(120)] public string Title { get; set; } = string.Empty;
    [MaxLength(2000)] public string? Description { get; set; }
    public AnimalType AnimalType { get; set; }
    public AnimalCondition Condition { get; set; }
    public SeverityLevel Severity { get; set; }
    [Range(-90, 90)] public double Latitude { get; set; }
    [Range(-180, 180)] public double Longitude { get; set; }
    [MaxLength(300)] public string? Address { get; set; }
    public List<IFormFile> Images { get; set; } = new();
}

[ApiController]
[Route("api/reports")]
[Authorize]
public class ReportsController : ControllerBase
{
    private readonly IReportService _reports;
    public ReportsController(IReportService reports) => _reports = reports;

    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(45 * 1024 * 1024)]
    public async Task<ActionResult<ReportDto>> Create([FromForm] CreateReportForm form, CancellationToken ct)
    {
        var request = new CreateReportRequest(form.Title, form.Description, form.AnimalType, form.Condition,
                                              form.Severity, form.Latitude, form.Longitude, form.Address);

        // Translate ASP.NET's IFormFile into the framework-free ImageUpload used by the Application layer.
        var streams = new List<Stream>();
        try
        {
            var uploads = form.Images.Select(f =>
            {
                var s = f.OpenReadStream();
                streams.Add(s);
                return new ImageUpload(s, f.FileName, f.ContentType, f.Length);
            }).ToList();

            var created = await _reports.CreateAsync(request, uploads, ct);
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
        }
        finally
        {
            foreach (var s in streams) await s.DisposeAsync();
        }
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ReportDto>> GetById(Guid id, CancellationToken ct)
        => Ok(await _reports.GetByIdAsync(id, ct));

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ReportDto>> Update(Guid id, UpdateReportRequest request, CancellationToken ct)
        => Ok(await _reports.UpdateAsync(id, request, ct));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _reports.DeleteAsync(id, ct);
        return NoContent();
    }

    /// <summary>Community numbers for the Home screen (reported / helped / active).</summary>
    [HttpGet("stats")]
    public async Task<ActionResult<CommunityStatsDto>> Stats(CancellationToken ct)
        => Ok(await _reports.GetCommunityStatsAsync(ct));

    /// <summary>Reports submitted by the logged-in user (Profile → History).</summary>
    [HttpGet("mine")]
    public async Task<ActionResult<IReadOnlyList<ReportSummaryDto>>> Mine(CancellationToken ct)
        => Ok(await _reports.GetMyReportsAsync(ct));

    /// <summary>FUTURE FEATURE: returns 501 Not Implemented for now.</summary>
    [HttpPost("{id:guid}/ai-analysis")]
    public async Task<ActionResult<HealthAnalysisResult>> AnalyzeHealth(Guid id, CancellationToken ct)
        => Ok(await _reports.AnalyzeHealthAsync(id, ct));
}
