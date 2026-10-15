using System.Text.Json.Serialization;

namespace VehicleOcr.Api.Dtos;

public class AnalyzeResponseDto
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0";

    [JsonPropertyName("success")]
    public bool Success { get; set; } = true;

    [JsonPropertyName("data")]
    public AnalyzeResultDataDto Data { get; set; } = new();

    [JsonPropertyName("meta")]
    public ApiMetaDto Meta { get; set; } = new();
}
