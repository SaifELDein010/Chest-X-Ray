using Chest_Xray.DTOs;
using Microsoft.AspNetCore.Http;

namespace Chest_Xray.Services;

public interface IReportService
{
    Task<ReportDto> UploadAndAnalyzeAsync(int userId, IFormFile image);
    Task<PagedReportResponseDto> GetReportsAsync(int userId, int page, int limit, string? status, string? search);
    Task<ReportDto?> GetReportByIdAsync(int userId, int reportId);
    Task<ReportDto?> UpdateReportNameAsync(int userId, int reportId, string reportName);
    Task<bool> DeleteReportAsync(int userId, int reportId);
    Task<byte[]?> GetOriginalImageAsync(int userId, int reportId);
    Task<byte[]?> GetHeatmapImageAsync(int userId, int reportId);
    Task<byte[]?> GeneratePdfAsync(int userId, int reportId);
}