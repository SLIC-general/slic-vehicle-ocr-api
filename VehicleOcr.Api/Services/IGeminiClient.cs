using VehicleOcr.Api.Dtos;

namespace VehicleOcr.Api.Services;

public record GeminiOcrResult(VehicleCrData Data, Dictionary<string, decimal> FieldConfidence);

public interface IGeminiClient
{
    Task<GeminiOcrResult> GenerateCrDataAsync(byte[] fileBytes, string mimeType, CancellationToken cancellationToken = default);
}
