namespace VehicleOcr.Api.Config;

public class GeminiOptions
{
    public const string SectionName = "Gemini";
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gemini-2.5-flash";
    public int TimeoutSeconds { get; set; } = 45;
    public int TotalTimeoutSeconds { get; set; } = 90;
    public int MaxAttempts { get; set; } = 3;
    public int InitialBackoffMs { get; set; } = 1000;
    public int MaxBackoffMs { get; set; } = 10000;
    public string? FallbackModel { get; set; }
}
