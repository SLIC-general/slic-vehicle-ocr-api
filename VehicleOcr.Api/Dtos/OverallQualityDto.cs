using System.Text.Json.Serialization;

namespace VehicleOcr.Api.Dtos;

public class OverallQualityDto
{
    [JsonPropertyName("score")]
    public decimal? Score { get; set; }

    [JsonPropertyName("grade")]
    public string? Grade { get; set; }

    [JsonPropertyName("needsReview")]
    public bool NeedsReview { get; set; } = false;

    [JsonPropertyName("flaggedFields")]
    public List<string> FlaggedFields { get; set; } = new();

    [JsonPropertyName("reasons")]
    public List<string> Reasons { get; set; } = new();
}
