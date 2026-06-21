using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Chest_Xray.DTOs;
using Chest_Xray.Services;

namespace Chest_Xray.Controllers;

[ApiController]
[Route("api/reports")]
[Authorize]
public class ReportsController : ControllerBase
{
    private readonly IReportService _reportService;

    public ReportsController(IReportService reportService)
    {
        _reportService = reportService;
    }

    private Guid GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier);
        return Guid.Parse(claim!.Value);
    }

    [HttpPost]
    public async Task<IActionResult> UploadAndAnalyze(IFormFile image)
    {
        try
        {
            var userId = GetUserId();
            var result = await _reportService.UploadAndAnalyzeAsync(userId, image);
            return StatusCode(201, result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = "Analysis failed", errors = new { Image = new[] { ex.Message } } });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetReports(
        [FromQuery] int page = 1,
        [FromQuery] int limit = 10,
        [FromQuery] string? status = null,
        [FromQuery] string? search = null)
    {
        var userId = GetUserId();
        var result = await _reportService.GetReportsAsync(userId, page, limit, status, search);
        return Ok(result);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetReportById(Guid id)
    {
        try
        {
            var userId = GetUserId();
            var result = await _reportService.GetReportByIdAsync(userId, id);

            if (result == null)
                return NotFound(new { message = "Report not found" });

            return Ok(result);
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new { message = "You are not authorized to access this report" });
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateReportName(Guid id, [FromBody] UpdateReportDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.ReportName))
            return BadRequest(new { message = "Invalid update", errors = new { ReportName = new[] { "Report name is required" } } });

        try
        {
            var userId = GetUserId();
            var result = await _reportService.UpdateReportNameAsync(userId, id, dto.ReportName);

            if (result == null)
                return NotFound(new { message = "Report not found" });

            return Ok(new { message = "Report updated successfully", report = result });
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new { message = "You are not authorized to update this report" });
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteReport(Guid id)
    {
        try
        {
            var userId = GetUserId();
            var result = await _reportService.DeleteReportAsync(userId, id);

            if (!result)
                return NotFound(new { message = "Report not found" });

            return NoContent();
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new { message = "You are not authorized to delete this report" });
        }
    }

    [HttpGet("{id:guid}/original-image")]
    public async Task<IActionResult> GetOriginalImage(Guid id)
    {
        try
        {
            var userId = GetUserId();
            var imageBytes = await _reportService.GetOriginalImageAsync(userId, id);

            if (imageBytes == null)
                return NotFound(new { message = "Image not found" });

            return File(imageBytes, "image/png");
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new { message = "You are not authorized to view this image" });
        }
    }

    [HttpGet("{id:guid}/heatmap-image")]
    public async Task<IActionResult> GetHeatmapImage(Guid id)
    {
        try
        {
            var userId = GetUserId();
            var imageBytes = await _reportService.GetHeatmapImageAsync(userId, id);

            if (imageBytes == null)
                return NotFound(new { message = "Image not found" });

            return File(imageBytes, "image/png");
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new { message = "You are not authorized to view this image" });
        }
    }

    [HttpGet("{id:guid}/pdf")]
    public async Task<IActionResult> DownloadPdf(Guid id)
    {
        try
        {
            var userId = GetUserId();
            var pdfBytes = await _reportService.GeneratePdfAsync(userId, id);

            if (pdfBytes == null)
                return NotFound(new { message = "Report not found" });

            return File(pdfBytes, "application/pdf", $"Report_{DateTime.UtcNow:yyyyMMdd}.pdf");
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new { message = "You are not authorized to download this report" });
        }
    }
}