namespace VehicleOcr.Api.Dtos;

public class QualityResult
{
    public double Score { get; set; }
    public string Grade { get; set; } = "GOOD";
    public bool NeedsReview { get; set; }
    public List<string> Reasons { get; set; } = new();
}
