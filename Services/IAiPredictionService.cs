using Chest_Xray.DTOs;

namespace Chest_Xray.Services;

public interface IAiPredictionService
{
    Task<AiPredictionResult> PredictAsync(string base64Image);
}