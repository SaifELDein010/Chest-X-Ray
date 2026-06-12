using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Chest_Xray.Data;
using Chest_Xray.DTOs;
using Chest_Xray.Models;

namespace Chest_Xray.Services;

public class ReportService : IReportService
{
    private readonly AppDbContext _context;
    private readonly IAiPredictionService _aiService;
    private readonly IWebHostEnvironment _env;

    public ReportService(AppDbContext context, IAiPredictionService aiService, IWebHostEnvironment env)
    {
        _context = context;
        _aiService = aiService;
        _env = env;
    }

    public async Task<ReportDto> UploadAndAnalyzeAsync(int userId, IFormFile image)
    {
        if (image == null || image.Length == 0)
            throw new ArgumentException("No image provided");

        if (image.Length > 50 * 1024 * 1024)
            throw new ArgumentException("File too large");

        var extension = Path.GetExtension(image.FileName).ToLower();
        if (extension != ".png" && extension != ".jpg" && extension != ".jpeg")
            throw new ArgumentException("Invalid format. Only PNG and JPG allowed");

        string base64Image;
        using (var ms = new MemoryStream())
        {
            await image.CopyToAsync(ms);
            base64Image = Convert.ToBase64String(ms.ToArray());
        }

        AiPredictionResult aiResult;
        aiResult = await _aiService.PredictAsync(base64Image);

        var (status, primaryDisease, overallConfidence) = DetermineReportStatus(aiResult.Predictions);

        var reportId = Guid.NewGuid().ToString();
        var originalFileName = $"original_{reportId}.png";
        var originalPath = Path.Combine("wwwroot", "uploads", "originals", originalFileName);

        using (var ms = new MemoryStream())
        {
            await image.CopyToAsync(ms);
            await File.WriteAllBytesAsync(originalPath, ms.ToArray());
        }

        var heatmapFileName = $"heatmap_{reportId}.png";
        var heatmapPath = Path.Combine("wwwroot", "uploads", "heatmaps", heatmapFileName);

        if (!string.IsNullOrEmpty(aiResult.HeatmapBase64))
        {
            var heatmapBytes = Convert.FromBase64String(aiResult.HeatmapBase64);
            await File.WriteAllBytesAsync(heatmapPath, heatmapBytes);
        }

        var report = new Report
        {
            UserId = userId,
            ReportName = $"Report_{DateTime.UtcNow:yyyyMMdd_HHmmss}",
            Status = status,
            PrimaryDisease = primaryDisease,
            OverallConfidence = overallConfidence,
            PredictionsJson = JsonSerializer.Serialize(aiResult.Predictions),
            OriginalImagePath = $"/uploads/originals/{originalFileName}",
            HeatmapImagePath = $"/uploads/heatmaps/{heatmapFileName}",
            CreatedAt = DateTime.UtcNow
        };

        _context.Reports.Add(report);
        await _context.SaveChangesAsync();

        return MapToReportDto(report);
    }

    public async Task<PagedReportResponseDto> GetReportsAsync(int userId, int page, int limit, string? status, string? search)
    {
        var query = _context.Reports.Where(r => r.UserId == userId);

        if (!string.IsNullOrEmpty(status))
            query = query.Where(r => r.Status.ToLower() == status.ToLower());

        if (!string.IsNullOrEmpty(search))
            query = query.Where(r => r.ReportName.Contains(search));

        var allReports = await _context.Reports.Where(r => r.UserId == userId).ToListAsync();
        var stats = new StatsDto
        {
            TotalReports = allReports.Count,
            TotalAbnormal = allReports.Count(r => r.Status == "Abnormal"),
            TotalHealthy = allReports.Count(r => r.Status == "Healthy"),
            AvgConfidence = allReports.Any() ? Math.Round(allReports.Average(r => r.OverallConfidence), 2) : 0
        };

        var totalItems = await query.CountAsync();
        var totalPages = (int)Math.Ceiling(totalItems / (double)limit);

        var reports = await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * limit)
            .Take(limit)
            .Select(r => new ReportListItemDto
            {
                Id = r.ReportId,
                Status = r.Status,
                PrimaryDisease = r.PrimaryDisease,
                OverallConfidence = r.OverallConfidence,
                OriginalImageUrl = $"/api/reports/{r.ReportId}/original-image",
                HeatmapImageUrl = $"/api/reports/{r.ReportId}/heatmap-image",
                CreatedAt = r.CreatedAt,
                ReportName = r.ReportName
            })
            .ToListAsync();

        return new PagedReportResponseDto
        {
            Stats = stats,
            Pagination = new PaginationDto
            {
                CurrentPage = page,
                Limit = limit,
                TotalPages = totalPages,
                TotalItems = totalItems
            },
            Reports = reports
        };
    }

    public async Task<ReportDto?> GetReportByIdAsync(int userId, int reportId)
    {
        var report = await _context.Reports
            .FirstOrDefaultAsync(r => r.ReportId == reportId && r.UserId == userId);

        return report == null ? null : MapToReportDto(report);
    }

    public async Task<ReportDto?> UpdateReportNameAsync(int userId, int reportId, string reportName)
    {
        var report = await _context.Reports
            .FirstOrDefaultAsync(r => r.ReportId == reportId && r.UserId == userId);

        if (report == null) return null;

        report.ReportName = reportName;
        await _context.SaveChangesAsync();

        return MapToReportDto(report);
    }

    public async Task<bool> DeleteReportAsync(int userId, int reportId)
    {
        var report = await _context.Reports
            .FirstOrDefaultAsync(r => r.ReportId == reportId && r.UserId == userId);

        if (report == null) return false;

        var originalPath = Path.Combine("wwwroot", report.OriginalImagePath.TrimStart('/'));
        var heatmapPath = Path.Combine("wwwroot", report.HeatmapImagePath.TrimStart('/'));

        if (File.Exists(originalPath)) File.Delete(originalPath);
        if (File.Exists(heatmapPath)) File.Delete(heatmapPath);

        _context.Reports.Remove(report);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<byte[]?> GetOriginalImageAsync(int userId, int reportId)
    {
        var report = await _context.Reports
            .FirstOrDefaultAsync(r => r.ReportId == reportId && r.UserId == userId);

        if (report == null) return null;

        var path = Path.Combine("wwwroot", report.OriginalImagePath.TrimStart('/'));
        return File.Exists(path) ? await File.ReadAllBytesAsync(path) : null;
    }

    public async Task<byte[]?> GetHeatmapImageAsync(int userId, int reportId)
    {
        var report = await _context.Reports
            .FirstOrDefaultAsync(r => r.ReportId == reportId && r.UserId == userId);

        if (report == null) return null;

        var path = Path.Combine("wwwroot", report.HeatmapImagePath.TrimStart('/'));
        return File.Exists(path) ? await File.ReadAllBytesAsync(path) : null;
    }

    public async Task<byte[]?> GeneratePdfAsync(int userId, int reportId)
    {
        var report = await _context.Reports
            .FirstOrDefaultAsync(r => r.ReportId == reportId && r.UserId == userId);

        if (report == null) return null;

        return GeneratePdf(report);
    }

    private (string status, string? primaryDisease, decimal overallConfidence) DetermineReportStatus(List<PredictionItem> predictions)
    {
        var detected = predictions.Where(p => p.Detected).ToList();

        if (!detected.Any())
        {
            var maxConf = predictions.Any() ? predictions.Max(p => p.Confidence) : 0;
            return ("Healthy", null, maxConf);
        }

        var highest = detected.OrderByDescending(p => p.Confidence).First();

        if (highest.Confidence >= 0.50m)
            return ("Abnormal", highest.Pathology, highest.Confidence);
        else
            return ("ReviewNeeded", highest.Pathology, highest.Confidence);
    }

    private ReportDto MapToReportDto(Report report)
    {
        var predictions = JsonSerializer.Deserialize<List<PredictionItem>>(report.PredictionsJson) ?? new();

        return new ReportDto
        {
            Id = report.ReportId,
            Status = report.Status,
            PrimaryDisease = report.PrimaryDisease,
            Predictions = predictions.Select(p => new PredictionDto
            {
                Pathology = p.Pathology,
                Confidence = p.Confidence,
                Detected = p.Detected
            }).ToList(),
            OverallConfidence = report.OverallConfidence,
            OriginalImageUrl = $"/api/reports/{report.ReportId}/original-image",
            HeatmapImageUrl = $"/api/reports/{report.ReportId}/heatmap-image",
            PdfDownloadUrl = $"/api/reports/{report.ReportId}/pdf",
            CreatedAt = report.CreatedAt
        };
    }

    private byte[] GeneratePdf(Report report)
    {
        using var ms = new MemoryStream();
        using var document = new iTextSharp.text.Document();
        var writer = iTextSharp.text.pdf.PdfWriter.GetInstance(document, ms);
        document.Open();

        var titleFont = new iTextSharp.text.Font(iTextSharp.text.Font.HELVETICA, 18, iTextSharp.text.Font.BOLD);
        var normalFont = new iTextSharp.text.Font(iTextSharp.text.Font.HELVETICA, 12, iTextSharp.text.Font.NORMAL);

        document.Add(new iTextSharp.text.Paragraph("Chest X-Ray Analysis Report", titleFont));
        document.Add(new iTextSharp.text.Paragraph(" "));

        document.Add(new iTextSharp.text.Paragraph($"Report Date: {report.CreatedAt:yyyy-MM-dd HH:mm}", normalFont));
        document.Add(new iTextSharp.text.Paragraph($"Status: {report.Status}", normalFont));
        document.Add(new iTextSharp.text.Paragraph($"Primary Disease: {report.PrimaryDisease ?? "None"}", normalFont));
        document.Add(new iTextSharp.text.Paragraph($"Confidence: {report.OverallConfidence:P2}", normalFont));
        document.Add(new iTextSharp.text.Paragraph(" "));

        var originalPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", report.OriginalImagePath.TrimStart('/'));

        if (File.Exists(originalPath))
        {
            var img = iTextSharp.text.Image.GetInstance(originalPath);
            img.ScaleToFit(300, 300);
            document.Add(img);
            document.Add(new iTextSharp.text.Paragraph(" "));
        }

        var heatmapPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", report.HeatmapImagePath.TrimStart('/'));

        if (File.Exists(heatmapPath))
        {
            document.Add(new iTextSharp.text.Paragraph("AI Analysis Heatmap:", normalFont));
            var heatmapImg = iTextSharp.text.Image.GetInstance(heatmapPath);
            heatmapImg.ScaleToFit(300, 300);
            document.Add(heatmapImg);
        }

        document.Close();
        return ms.ToArray();
    }

}