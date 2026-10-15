namespace VehicleOcr.Api.Exceptions;

public class GeminiException : Exception
{
    public int? StatusCode { get; }

    public GeminiException(string message) : base(message) { }

    public GeminiException(string message, int statusCode) : base(message)
    {
        StatusCode = statusCode;
    }

    public GeminiException(string message, Exception innerException) : base(message, innerException) { }

    public GeminiException(string message, int statusCode, Exception innerException) : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
