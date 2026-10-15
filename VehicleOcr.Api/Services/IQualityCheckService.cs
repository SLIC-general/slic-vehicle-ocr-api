using VehicleOcr.Api.Dtos;

namespace VehicleOcr.Api.Services;

public interface IQualityCheckService
{
    Task<QualityResult> CheckAsync(byte[] fileBytes, string contentType);
}
