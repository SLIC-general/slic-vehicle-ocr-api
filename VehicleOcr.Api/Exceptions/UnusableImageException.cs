using VehicleOcr.Api.Dtos;

namespace VehicleOcr.Api.Exceptions;

public class UnusableImageException : Exception
{
    public List<string> Reasons { get; }
    public QualityResult? QualityResult { get; }

    public UnusableImageException(List<string> reasons, QualityResult? qualityResult = null)
        : base("The document quality is too low to process.")
    {
        Reasons = reasons ?? new List<string>();
        QualityResult = qualityResult;
    }
}
