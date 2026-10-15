using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using VehicleOcr.Api.Exceptions;
using VehicleOcr.Api.Services;

namespace VehicleOcr.Api.Controllers;

[ApiController]
[Route("api/v1/ocr")]
[Route("api/ocr")]
public class OcrController : ControllerBase
{
    private readonly IOcrService _ocrService;
    private readonly IConfiguration _config;
    private readonly ILogger<OcrController> _logger;

    public OcrController(IOcrService ocrService, IConfiguration config, ILogger<OcrController> logger)
    {
        _ocrService = ocrService;
        _config = config;
        _logger = logger;
    }

    [HttpGet("health")]
    public IActionResult Health()
    {
        var model = _config["GEMINI_MODEL"] ?? "gemini-3-flash-preview";
        return Ok(new
        {
            version = "1.0",
            status = "ok",
            model = model,
            database = "not_used"
        });
    }

    [HttpPost("analyze")]
    [EnableRateLimiting("ocr")]
    public async Task<IActionResult> Analyze(IFormFile? file)
    {
        if (file == null || file.Length == 0)
        {
            throw new InvalidFileException("File field 'file' is required in form-data.");
        }

        var result = await _ocrService.ProcessCrDocumentAsync(file);
        return Ok(result);
    }
}
