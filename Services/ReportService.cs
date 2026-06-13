using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Chest_Xray.Data;
using Chest_Xray.DTOs;
using Chest_Xray.Models;
using iTextSharp.text;
using iTextSharp.text.pdf;

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

        // Model mockup
        AiPredictionResult aiResult;
        try
        {
            aiResult = await _aiService.PredictAsync(base64Image);
        }
        catch (Exception)
        {
            aiResult = GetMockPrediction();
        }

        // Model connection
        //AiPredictionResult aiResult;
        //aiResult = await _aiService.PredictAsync(base64Image);

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
    using var document = new iTextSharp.text.Document(iTextSharp.text.PageSize.A4, 30, 30, 30, 30);
    var writer = iTextSharp.text.pdf.PdfWriter.GetInstance(document, ms);
    document.Open();

    var darkBlue = new iTextSharp.text.BaseColor(0, 51, 102);
    var red = new iTextSharp.text.BaseColor(220, 53, 69);
    var green = new iTextSharp.text.BaseColor(25, 135, 84);
    var yellow = new iTextSharp.text.BaseColor(255, 193, 7);
    var lightRed = new iTextSharp.text.BaseColor(255, 245, 245);
    var black = new iTextSharp.text.BaseColor(0, 0, 0);
    var white = new iTextSharp.text.BaseColor(255, 255, 255);
    var gray = new iTextSharp.text.BaseColor(128, 128, 128);
    var lightGray = new iTextSharp.text.BaseColor(211, 211, 211);

    var titleFont = new iTextSharp.text.Font(iTextSharp.text.Font.HELVETICA, 22, iTextSharp.text.Font.BOLD, darkBlue);
    var headerFont = new iTextSharp.text.Font(iTextSharp.text.Font.HELVETICA, 14, iTextSharp.text.Font.BOLD, darkBlue);
    var normalFont = new iTextSharp.text.Font(iTextSharp.text.Font.HELVETICA, 11, iTextSharp.text.Font.NORMAL, black);
    var boldFont = new iTextSharp.text.Font(iTextSharp.text.Font.HELVETICA, 11, iTextSharp.text.Font.BOLD, black);
    var smallFont = new iTextSharp.text.Font(iTextSharp.text.Font.HELVETICA, 9, iTextSharp.text.Font.NORMAL, gray);

    var headerTable = new iTextSharp.text.pdf.PdfPTable(2);
    headerTable.WidthPercentage = 100;
    headerTable.SetWidths(new float[] { 70, 30 });

    var logoCell = new iTextSharp.text.pdf.PdfPCell(new iTextSharp.text.Phrase("Chest X-Ray AI Analysis", titleFont));
    logoCell.Border = iTextSharp.text.Rectangle.NO_BORDER;
    logoCell.HorizontalAlignment = iTextSharp.text.Element.ALIGN_LEFT;
    headerTable.AddCell(logoCell);

    var dateCell = new iTextSharp.text.pdf.PdfPCell(new iTextSharp.text.Phrase($"Date: {report.CreatedAt:yyyy-MM-dd HH:mm}", smallFont));
    dateCell.Border = iTextSharp.text.Rectangle.NO_BORDER;
    dateCell.HorizontalAlignment = iTextSharp.text.Element.ALIGN_RIGHT;
    headerTable.AddCell(dateCell);

    document.Add(headerTable);

    var line = new iTextSharp.text.pdf.draw.LineSeparator(1f, 100f, lightGray, iTextSharp.text.Element.ALIGN_CENTER, -2);
    document.Add(new iTextSharp.text.Chunk(line));
    document.Add(new iTextSharp.text.Paragraph(" "));

    var statusColor = report.Status switch
    {
        "Abnormal" => red,
        "Healthy" => green,
        "ReviewNeeded" => yellow,
        _ => black
    };

    var infoTable = new iTextSharp.text.pdf.PdfPTable(2);
    infoTable.WidthPercentage = 100;
    infoTable.SetWidths(new float[] { 40, 60 });
    infoTable.SpacingAfter = 15;

    infoTable.AddCell(CreateInfoCell("Status:", boldFont));
    var statusCell = new iTextSharp.text.pdf.PdfPCell(new iTextSharp.text.Phrase(report.Status, new iTextSharp.text.Font(iTextSharp.text.Font.HELVETICA, 12, iTextSharp.text.Font.BOLD, statusColor)));
    statusCell.Border = iTextSharp.text.Rectangle.NO_BORDER;
    statusCell.PaddingBottom = 8;
    infoTable.AddCell(statusCell);

    infoTable.AddCell(CreateInfoCell("Primary Disease:", boldFont));
    infoTable.AddCell(CreateInfoCell(report.PrimaryDisease ?? "None Detected", normalFont));

    infoTable.AddCell(CreateInfoCell("Confidence Score:", boldFont));
    infoTable.AddCell(CreateInfoCell($"{report.OverallConfidence:P2}", normalFont));

    infoTable.AddCell(CreateInfoCell("Report ID:", boldFont));
    infoTable.AddCell(CreateInfoCell($"#{report.ReportId}", smallFont));

    document.Add(infoTable);

    document.Add(new iTextSharp.text.Paragraph("Pathology Predictions", headerFont));
    document.Add(new iTextSharp.text.Paragraph(" "));

    var predTable = new iTextSharp.text.pdf.PdfPTable(4);
    predTable.WidthPercentage = 100;
    predTable.SetWidths(new float[] { 35, 20, 20, 25 });
    predTable.SpacingAfter = 20;

    predTable.AddCell(CreateHeaderCell("Pathology", darkBlue));
    predTable.AddCell(CreateHeaderCell("Confidence", darkBlue));
    predTable.AddCell(CreateHeaderCell("Threshold", darkBlue));
    predTable.AddCell(CreateHeaderCell("Status", darkBlue));

    var predictions = JsonSerializer.Deserialize<List<PredictionItem>>(report.PredictionsJson) ?? new();

    foreach (var pred in predictions)
    {
        var bgColor = pred.Detected ? lightRed : white;

        predTable.AddCell(CreateDataCell(pred.Pathology, normalFont, bgColor));
        predTable.AddCell(CreateDataCell($"{pred.Confidence:P2}", normalFont, bgColor));
        predTable.AddCell(CreateDataCell("--", smallFont, bgColor));

        var detectedText = pred.Detected ? "✓ Detected" : "✗ Not Detected";
        var detectedColor = pred.Detected ? red : green;
        predTable.AddCell(CreateDataCell(detectedText, new iTextSharp.text.Font(iTextSharp.text.Font.HELVETICA, 10, iTextSharp.text.Font.BOLD, detectedColor), bgColor));
    }

    document.Add(predTable);

    document.Add(new iTextSharp.text.Paragraph("X-Ray Analysis Images", headerFont));
    document.Add(new iTextSharp.text.Paragraph(" "));

    var originalPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", report.OriginalImagePath.TrimStart('/'));
    var heatmapPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", report.HeatmapImagePath.TrimStart('/'));

    var imageTable = new iTextSharp.text.pdf.PdfPTable(2);
    imageTable.WidthPercentage = 100;
    imageTable.SetWidths(new float[] { 50, 50 });

    if (File.Exists(originalPath))
    {
        var originalImg = iTextSharp.text.Image.GetInstance(originalPath);
        originalImg.ScaleToFit(250, 250);
        var originalCell = new iTextSharp.text.pdf.PdfPCell(originalImg);
        originalCell.HorizontalAlignment = iTextSharp.text.Element.ALIGN_CENTER;
        originalCell.Border = iTextSharp.text.Rectangle.BOX;
        originalCell.BorderColor = lightGray;
        originalCell.Padding = 8;
        imageTable.AddCell(originalCell);
    }
    else
    {
        imageTable.AddCell(CreateDataCell("Not available", smallFont, white));
    }

    if (File.Exists(heatmapPath))
    {
        var heatmapImg = iTextSharp.text.Image.GetInstance(heatmapPath);
        heatmapImg.ScaleToFit(250, 250);
        var heatmapCell = new iTextSharp.text.pdf.PdfPCell(heatmapImg);
        heatmapCell.HorizontalAlignment = iTextSharp.text.Element.ALIGN_CENTER;
        heatmapCell.Border = iTextSharp.text.Rectangle.BOX;
        heatmapCell.BorderColor = lightGray;
        heatmapCell.Padding = 8;
        imageTable.AddCell(heatmapCell);
    }
    else
    {
        imageTable.AddCell(CreateDataCell("Not available", smallFont, white));
    }

    var captionTable = new iTextSharp.text.pdf.PdfPTable(2);
    captionTable.WidthPercentage = 100;
    captionTable.SetWidths(new float[] { 50, 50 });

    captionTable.AddCell(CreateCaptionCell("Original X-Ray Image"));
    captionTable.AddCell(CreateCaptionCell("AI Heatmap Overlay"));

    document.Add(imageTable);
    document.Add(captionTable);

    document.Add(new iTextSharp.text.Paragraph(" "));
    var footerLine = new iTextSharp.text.pdf.draw.LineSeparator(1f, 100f, lightGray, iTextSharp.text.Element.ALIGN_CENTER, -2);
    document.Add(new iTextSharp.text.Chunk(footerLine));
    document.Add(new iTextSharp.text.Paragraph(" "));

    var footer = new iTextSharp.text.Paragraph("Generated by Chest X-Ray AI Analysis System | For clinical decision support only.", smallFont);
    footer.Alignment = iTextSharp.text.Element.ALIGN_CENTER;
    document.Add(footer);

    document.Close();
    return ms.ToArray();
    }

    private iTextSharp.text.pdf.PdfPCell CreateInfoCell(string text, iTextSharp.text.Font font)
    {
        var cell = new iTextSharp.text.pdf.PdfPCell(new iTextSharp.text.Phrase(text, font));
        cell.Border = iTextSharp.text.Rectangle.NO_BORDER;
        cell.PaddingBottom = 6;
        return cell;
    }

    private iTextSharp.text.pdf.PdfPCell CreateHeaderCell(string text, iTextSharp.text.BaseColor bgColor)
    {
        var cell = new iTextSharp.text.pdf.PdfPCell(new iTextSharp.text.Phrase(text, new iTextSharp.text.Font(iTextSharp.text.Font.HELVETICA, 11, iTextSharp.text.Font.BOLD, new iTextSharp.text.BaseColor(255, 255, 255))));
        cell.BackgroundColor = bgColor;
        cell.HorizontalAlignment = iTextSharp.text.Element.ALIGN_CENTER;
        cell.VerticalAlignment = iTextSharp.text.Element.ALIGN_MIDDLE;
        cell.Padding = 8;
        cell.Border = iTextSharp.text.Rectangle.NO_BORDER;
        return cell;
    }

    private iTextSharp.text.pdf.PdfPCell CreateDataCell(string text, iTextSharp.text.Font font, iTextSharp.text.BaseColor bgColor)
    {
        var cell = new iTextSharp.text.pdf.PdfPCell(new iTextSharp.text.Phrase(text, font));
        cell.BackgroundColor = bgColor;
        cell.HorizontalAlignment = iTextSharp.text.Element.ALIGN_CENTER;
        cell.VerticalAlignment = iTextSharp.text.Element.ALIGN_MIDDLE;
        cell.Padding = 6;
        cell.Border = iTextSharp.text.Rectangle.BOTTOM_BORDER;
        cell.BorderColor = new iTextSharp.text.BaseColor(211, 211, 211);
        return cell;
    }

    private iTextSharp.text.pdf.PdfPCell CreateCaptionCell(string text)
    {
        var cell = new iTextSharp.text.pdf.PdfPCell(new iTextSharp.text.Phrase(text, new iTextSharp.text.Font(iTextSharp.text.Font.HELVETICA, 10, iTextSharp.text.Font.ITALIC, new iTextSharp.text.BaseColor(128, 128, 128))));
        cell.HorizontalAlignment = iTextSharp.text.Element.ALIGN_CENTER;
        cell.Border = iTextSharp.text.Rectangle.NO_BORDER;
        cell.PaddingTop = 5;
        return cell;
    }

    // Mock Data Fallback
    private AiPredictionResult GetMockPrediction()
    {
        return new AiPredictionResult
        {
            Predictions = new List<PredictionItem>
            {
                new() { Pathology = "Atelectasis", Confidence = 0.85m, Detected = true },
                new() { Pathology = "Effusion", Confidence = 0.25m, Detected = false },
                new() { Pathology = "Pneumothorax", Confidence = 0.10m, Detected = false }
            },
            HeatmapBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg=="
        };
    }
}