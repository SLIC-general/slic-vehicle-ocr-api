using Microsoft.AspNetCore.Http;

namespace VehicleOcr.Api.Dtos;

public class AnalyzeRequestDto
{
    public IFormFile? File { get; set; }
}
