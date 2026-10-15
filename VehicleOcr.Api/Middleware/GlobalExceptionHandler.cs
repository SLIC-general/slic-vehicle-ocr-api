using System.Text.Json;
using VehicleOcr.Api.Dtos;
using VehicleOcr.Api.Exceptions;

namespace VehicleOcr.Api.Middleware;

public class GlobalExceptionHandler
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(RequestDelegate next, ILogger<GlobalExceptionHandler> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception ex)
    {
        var response = context.Response;
        response.ContentType = "application/json";

        var (statusCode, errorCode) = ex switch
        {
            InvalidFileException => (StatusCodes.Status400BadRequest, "INVALID_FILE"),
            FileTooLargeException => (StatusCodes.Status413PayloadTooLarge, "TOO_LARGE"),
            UnusableImageException => (StatusCodes.Status422UnprocessableEntity, "UNUSABLE_IMAGE"),
            OcrFailedException => (StatusCodes.Status422UnprocessableEntity, "OCR_FAILED"),
            GeminiException => (StatusCodes.Status502BadGateway, "GEMINI_ERROR"),
            _ => (StatusCodes.Status500InternalServerError, "INTERNAL_ERROR")
        };

        if (statusCode >= 500)
        {
            _logger.LogError(ex, "Server error: {Message}", ex.Message);
        }
        else
        {
            _logger.LogWarning("Handled error [{Code}]: {Message}", errorCode, ex.Message);
        }

        response.StatusCode = statusCode;
        
        var detailsPayload = new ErrorDetailsPayloadDto();
        if (ex is UnusableImageException unusableEx)
        {
            detailsPayload.Reasons = unusableEx.Reasons;
            if (unusableEx.QualityResult != null)
            {
                detailsPayload.OverallQuality = new OverallQualityDto
                {
                    Score = (decimal)unusableEx.QualityResult.Score,
                    Grade = unusableEx.QualityResult.Grade,
                    NeedsReview = unusableEx.QualityResult.NeedsReview,
                    Reasons = unusableEx.QualityResult.Reasons
                };
            }
        }

        var errorDto = new ErrorResponseDto
        {
            Version = "1.0",
            Success = false,
            Error = new ErrorDetailDto
            {
                Code = errorCode,
                Message = ex.Message,
                Status = statusCode,
                Details = detailsPayload
            }
        };

        var json = JsonSerializer.Serialize(errorDto, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        await response.WriteAsync(json);
    }
}
