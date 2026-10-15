using System.Text.Json.Serialization;

namespace VehicleOcr.Api.Dtos;

public class AnalyzeResultDataDto
{
    [JsonPropertyName("fields")]
    public VehicleCrData Fields { get; set; } = new();

    [JsonPropertyName("fieldConfidence")]
    public Dictionary<string, decimal> FieldConfidence { get; set; } = new();

    [JsonPropertyName("overallQuality")]
    public OverallQualityDto? OverallQuality { get; set; }

    [JsonPropertyName("extractedAt")]
    public DateTime ExtractedAt { get; set; }
}

public class ApiMetaDto
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("durationMs")]
    public long DurationMs { get; set; }
}
