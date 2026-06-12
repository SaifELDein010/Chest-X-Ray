namespace Chest_Xray.DTOs;

public class AiPredictionResult
{
    public List<PredictionItem> Predictions { get; set; } = new();
    public string HeatmapBase64 { get; set; } = string.Empty;
}

public class PredictionItem
{
    public string Pathology { get; set; } = string.Empty;
    public decimal Confidence { get; set; }
    public bool Detected { get; set; }
}