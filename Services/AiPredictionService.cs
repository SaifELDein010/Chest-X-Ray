using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Chest_Xray.DTOs;

namespace Chest_Xray.Services;

public class AiPredictionService : IAiPredictionService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    public AiPredictionService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
    }

    public async Task<AiPredictionResult> PredictAsync(string base64Image)
    {
        var aiServiceUrl = _configuration["AiService:Url"] ?? "http://localhost:8000/api/predict";
        var apiKey = _configuration["AiService:ApiKey"] ?? "";

        var payload = new
        {
            image = base64Image,
            image_format = "png"
        };

        var request = new HttpRequestMessage(HttpMethod.Post, aiServiceUrl)
        {
            Content = JsonContent.Create(payload)
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"AI Service Error ({response.StatusCode}): {errorBody}");
        }

        var aiResponse = await response.Content.ReadFromJsonAsync<AiResponseDto>();

        if (aiResponse == null)
            throw new InvalidOperationException("AI service returned null");

        return MapToAiPredictionResult(aiResponse);
    }

    private AiPredictionResult MapToAiPredictionResult(AiResponseDto aiResponse)
    {
        return new AiPredictionResult
        {
            Predictions = aiResponse.Predictions?.Select(p => new PredictionItem
            {
                Pathology = p.Pathology ?? "Unknown",
                Confidence = p.Confidence,
                Detected = p.Detected
            }).ToList() ?? new(),

            HeatmapBase64 = aiResponse.Heatmap?.ImageBase64 ?? ""
        };
    }
}

public class AiResponseDto
{
    [JsonPropertyName("request_id")]
    public string? RequestId { get; set; }

    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; set; }

    [JsonPropertyName("predictions")]
    public List<AiPredictionItemDto>? Predictions { get; set; }

    [JsonPropertyName("heatmap")]
    public AiHeatmapDto? Heatmap { get; set; }

    [JsonPropertyName("processing_time_ms")]
    public double ProcessingTimeMs { get; set; }

    [JsonPropertyName("model_info")]
    public AiModelInfoDto? ModelInfo { get; set; }
}

public class AiPredictionItemDto
{
    [JsonPropertyName("pathology")]
    public string? Pathology { get; set; }

    [JsonPropertyName("confidence")]
    public decimal Confidence { get; set; }

    [JsonPropertyName("threshold")]
    public decimal Threshold { get; set; }

    [JsonPropertyName("detected")]
    public bool Detected { get; set; }
}

public class AiHeatmapDto
{
    [JsonPropertyName("image_base64")]
    public string? ImageBase64 { get; set; }

    [JsonPropertyName("pathology")]
    public string? Pathology { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }
}

public class AiModelInfoDto
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("device")]
    public string? Device { get; set; }

    [JsonPropertyName("is_loaded")]
    public bool IsLoaded { get; set; }

    [JsonPropertyName("num_classes")]
    public int NumClasses { get; set; }

    [JsonPropertyName("pathologies")]
    public List<string>? Pathologies { get; set; }

    [JsonPropertyName("image_size")]
    public int ImageSize { get; set; }

    [JsonPropertyName("thresholds")]
    public Dictionary<string, decimal>? Thresholds { get; set; }
}