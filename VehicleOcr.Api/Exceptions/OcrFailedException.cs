namespace VehicleOcr.Api.Exceptions;

public class OcrFailedException : Exception
{
    public OcrFailedException(string message) : base(message) { }
}
