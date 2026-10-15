using System.Text.Json.Serialization;

namespace VehicleOcr.Api.Dtos;

public class ErrorDetailsPayloadDto
{
    [JsonPropertyName("overallQuality")]
    public OverallQualityDto? OverallQuality { get; set; }

    [JsonPropertyName("reasons")]
    public List<string>? Reasons { get; set; }
}

public class ErrorDetailDto
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public int Status { get; set; }

    [JsonPropertyName("details")]
    public ErrorDetailsPayloadDto? Details { get; set; }
}

public class ErrorResponseDto
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0";

    [JsonPropertyName("success")]
    public bool Success { get; set; } = false;

    [JsonPropertyName("error")]
    public ErrorDetailDto Error { get; set; } = new();
}
