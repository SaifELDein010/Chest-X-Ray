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

    private int GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier);
        return int.Parse(claim!.Value);
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
    public async Task<IActionResult> GetReportById(int id)
    {
        var userId = GetUserId();
        var result = await _reportService.GetReportByIdAsync(userId, id);

        if (result == null)
            return NotFound(new { message = "Report not found" });

        return Ok(result);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateReportName(int id, [FromBody] UpdateReportDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.ReportName))
            return BadRequest(new { message = "Invalid update", errors = new { ReportName = new[] { "Report name is required" } } });

        var userId = GetUserId();
        var result = await _reportService.UpdateReportNameAsync(userId, id, dto.ReportName);

        if (result == null)
            return NotFound(new { message = "Report not found" });

        return Ok(new { message = "Report updated successfully", report = result });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteReport(int id)
    {
        var userId = GetUserId();
        var result = await _reportService.DeleteReportAsync(userId, id);

        if (!result)
            return NotFound(new { message = "Report not found" });

        return NoContent();
    }

    [HttpGet("{id}/original-image")]
    public async Task<IActionResult> GetOriginalImage(int id)
    {
        var userId = GetUserId();
        var imageBytes = await _reportService.GetOriginalImageAsync(userId, id);

        if (imageBytes == null)
            return NotFound(new { message = "Image not found" });

        return File(imageBytes, "image/png");
    }

    [HttpGet("{id}/heatmap-image")]
    public async Task<IActionResult> GetHeatmapImage(int id)
    {
        var userId = GetUserId();
        var imageBytes = await _reportService.GetHeatmapImageAsync(userId, id);

        if (imageBytes == null)
            return NotFound(new { message = "Image not found" });

        return File(imageBytes, "image/png");
    }

    [HttpGet("{id}/pdf")]
    public async Task<IActionResult> DownloadPdf(int id)
    {
        var userId = GetUserId();
        var pdfBytes = await _reportService.GeneratePdfAsync(userId, id);

        if (pdfBytes == null)
            return NotFound(new { message = "Report not found" });

        return File(pdfBytes, "application/pdf", $"Report_{DateTime.UtcNow:yyyyMMdd}.pdf");
    }
}