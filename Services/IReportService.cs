using Chest_Xray.DTOs;
using Microsoft.AspNetCore.Http;

namespace Chest_Xray.Services;

public interface IReportService
{
    Task<ReportDto> UploadAndAnalyzeAsync(Guid userId, IFormFile image);
    Task<PagedReportResponseDto> GetReportsAsync(Guid userId, int page, int limit, string? status, string? search);
    Task<ReportDto?> GetReportByIdAsync(Guid userId, Guid reportId);
    Task<ReportDto?> UpdateReportNameAsync(Guid userId, Guid reportId, string reportName);
    Task<bool> DeleteReportAsync(Guid userId, Guid reportId);
    Task<byte[]?> GetOriginalImageAsync(Guid userId, Guid reportId);
    Task<byte[]?> GetHeatmapImageAsync(Guid userId, Guid reportId);
    Task<byte[]?> GeneratePdfAsync(Guid userId, Guid reportId);
}