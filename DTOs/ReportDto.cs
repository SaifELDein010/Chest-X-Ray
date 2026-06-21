namespace Chest_Xray.DTOs;

public class ReportDto
{
    public Guid Id { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? PrimaryDisease { get; set; }
    public List<PredictionDto> Predictions { get; set; } = new();
    public decimal OverallConfidence { get; set; }
    public string OriginalImageUrl { get; set; } = string.Empty;
    public string HeatmapImageUrl { get; set; } = string.Empty;
    public string PdfDownloadUrl { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class PredictionDto
{
    public string Pathology { get; set; } = string.Empty;
    public decimal Confidence { get; set; }
    public bool Detected { get; set; }
}