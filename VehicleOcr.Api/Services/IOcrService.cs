using VehicleOcr.Api.Dtos;

namespace VehicleOcr.Api.Services;

public interface IOcrService
{
    Task<AnalyzeResponseDto> ProcessCrDocumentAsync(IFormFile file);
}
