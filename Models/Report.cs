namespace Chest_Xray.Models;

public class Report
{
    public int ReportId { get; set; }
    public int UserId { get; set; }
    public string ReportName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? PrimaryDisease { get; set; }
    public decimal OverallConfidence { get; set; }
    public string PredictionsJson { get; set; } = "[]";
    public string OriginalImagePath { get; set; } = string.Empty;
    public string HeatmapImagePath { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
}