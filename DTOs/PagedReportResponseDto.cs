namespace Chest_Xray.DTOs;

public class PagedReportResponseDto
{
    public StatsDto Stats { get; set; } = new();
    public PaginationDto Pagination { get; set; } = new();
    public List<ReportListItemDto> Reports { get; set; } = new();
}

public class StatsDto
{
    public int TotalReports { get; set; }
    public int TotalAbnormal { get; set; }
    public int TotalHealthy { get; set; }
    public decimal AvgConfidence { get; set; }
}

public class PaginationDto
{
    public int CurrentPage { get; set; }
    public int Limit { get; set; }
    public int TotalPages { get; set; }
    public int TotalItems { get; set; }
}

public class ReportListItemDto
{
    public int Id { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? PrimaryDisease { get; set; }
    public decimal OverallConfidence { get; set; }
    public string OriginalImageUrl { get; set; } = string.Empty;
    public string HeatmapImageUrl { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string ReportName { get; set; } = string.Empty;
}