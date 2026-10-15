namespace VehicleOcr.Api.Config;

public class QualityCheckOptions
{
    public int MinWidth { get; set; } = 800;
    public int MinHeight { get; set; } = 600;
    public double MinBlurScore { get; set; } = 100.0;
    public double MinBrightness { get; set; } = 40.0;
    public double MaxBrightness { get; set; } = 220.0;
    public double MinContrast { get; set; } = 20.0;
    public double MinQualityScore { get; set; } = 0.40;
    public double LowFieldConfidence { get; set; } = 0.60;
}
