using System.Diagnostics;
using VehicleOcr.Api.Config;
using VehicleOcr.Api.Dtos;
using VehicleOcr.Api.Exceptions;

namespace VehicleOcr.Api.Services;

public class OcrService : IOcrService
{
    private readonly IGeminiClient _geminiClient;
    private readonly IQualityCheckService _qualityCheckService;
    private readonly QualityCheckOptions _qualityOptions;
    private readonly IConfiguration _config;
    private readonly ILogger<OcrService> _logger;

    private static readonly HashSet<string> AllowedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/webp",
        "application/pdf"
    };

    public OcrService(
        IGeminiClient geminiClient,
        IQualityCheckService qualityCheckService,
        QualityCheckOptions qualityOptions,
        IConfiguration config,
        ILogger<OcrService> logger)
    {
        _geminiClient = geminiClient;
        _qualityCheckService = qualityCheckService;
        _qualityOptions = qualityOptions;
        _config = config;
        _logger = logger;
    }

    public async Task<AnalyzeResponseDto> ProcessCrDocumentAsync(IFormFile file)
    {
        using var stream = file.OpenReadStream();
        return await ProcessAsync(stream, file.FileName, file.ContentType, file.Length);
    }

    public async Task<AnalyzeResponseDto> ProcessAsync(Stream fileStream, string fileName, string contentType, long fileSizeBytes)
    {
        var sw = Stopwatch.StartNew();

        if (string.IsNullOrWhiteSpace(contentType) || !AllowedMimeTypes.Contains(contentType))
        {
            throw new InvalidFileException($"Unsupported content type '{contentType}'. Allowed types: JPEG, PNG, WEBP, PDF.");
        }

        var maxMbStr = _config["MAX_FILE_SIZE_MB"] ?? "10";
        var maxMb = long.TryParse(maxMbStr, out var val) ? val : 10;
        var maxBytes = maxMb * 1024 * 1024;

        if (fileSizeBytes > maxBytes)
        {
            throw new FileTooLargeException($"File size {fileSizeBytes / 1024 / 1024} MB exceeds the configured limit of {maxMb} MB.");
        }

        byte[] fileBytes;
        using (var ms = new MemoryStream())
        {
            await fileStream.CopyToAsync(ms);
            fileBytes = ms.ToArray();
        }

        if (fileBytes.Length == 0)
        {
            throw new InvalidFileException("The uploaded file is empty.");
        }

        // Quality assessment BEFORE calling Gemini
        var qualityResult = await _qualityCheckService.CheckAsync(fileBytes, contentType);
        if (qualityResult.Grade == "UNUSABLE")
        {
            _logger.LogWarning("File '{FileName}' rejected as UNUSABLE. Reasons: {Reasons}", fileName, string.Join("; ", qualityResult.Reasons));
            throw new UnusableImageException(qualityResult.Reasons, qualityResult);
        }

        var extractedAt = DateTime.UtcNow;
        var geminiResult = await _geminiClient.GenerateCrDataAsync(fileBytes, contentType);
        var data = geminiResult.Data;
        var rawFieldConfidence = geminiResult.FieldConfidence;

        // Build dictionary of all 28 fields to compute confidence and flagged fields
        var fieldMap = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["registrationNumber"] = data.RegistrationNumber,
            ["registrationDate"] = data.RegistrationDate,
            ["chassisNumber"] = data.ChassisNumber,
            ["engineNumber"] = data.EngineNumber,
            ["ownerName"] = data.OwnerName,
            ["fatherHusbandName"] = data.FatherHusbandName,
            ["currentAddress"] = data.CurrentAddress,
            ["permanentAddress"] = data.PermanentAddress,
            ["vehicleClass"] = data.VehicleClass,
            ["makersName"] = data.MakersName,
            ["modelName"] = data.ModelName,
            ["bodyType"] = data.BodyType,
            ["mfgMonthYear"] = data.MfgMonthYear,
            ["noOfCylinders"] = data.NoOfCylinders,
            ["cubicCapacity"] = data.CubicCapacity,
            ["horsePower"] = data.HorsePower,
            ["fuelType"] = data.FuelType,
            ["seatingCapacity"] = data.SeatingCapacity,
            ["standingCapacity"] = data.StandingCapacity,
            ["unladenWeight"] = data.UnladenWeight,
            ["grossWeight"] = data.GrossWeight,
            ["color"] = data.Color,
            ["wheelbase"] = data.Wheelbase,
            ["taxValidity"] = data.TaxValidity,
            ["fitnessValidity"] = data.FitnessValidity,
            ["insuranceDetails"] = data.InsuranceDetails,
            ["hypothecation"] = data.Hypothecation,
            ["issuingAuthority"] = data.IssuingAuthority
        };

        var baseConfidence = data.ConfidenceScore > 0 ? (decimal)data.ConfidenceScore : 0.98m;
        var computedFieldConfidence = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var flaggedFields = new List<string>();

        foreach (var (fieldName, fieldValue) in fieldMap)
        {
            bool hasValue = !string.IsNullOrWhiteSpace(fieldValue);
            decimal conf = 0.0m;

            if (hasValue)
            {
                if (rawFieldConfidence.TryGetValue(fieldName, out var geminiConf) && geminiConf > 0)
                {
                    conf = geminiConf;
                }
                else
                {
                    conf = baseConfidence;
                }
            }
            else
            {
                conf = 0.0m;
            }

            computedFieldConfidence[fieldName] = conf;

            // BUG 3 fix: flaggedFields must ONLY contain fields where:
            // - confidence < 0.60
            // - AND the field has a non-empty value
            // Empty fields are NOT flagged (just absent)
            if (hasValue && conf < (decimal)_qualityOptions.LowFieldConfidence)
            {
                flaggedFields.Add(fieldName);
            }
        }

        var overallQuality = new OverallQualityDto
        {
            Score = (decimal)qualityResult.Score,
            Grade = qualityResult.Grade,
            NeedsReview = qualityResult.NeedsReview || flaggedFields.Count > 0,
            FlaggedFields = flaggedFields,
            Reasons = qualityResult.Reasons
        };

        sw.Stop();

        return new AnalyzeResponseDto
        {
            Version = "1.0",
            Success = true,
            Data = new AnalyzeResultDataDto
            {
                Fields = data,
                FieldConfidence = computedFieldConfidence,
                OverallQuality = overallQuality,
                ExtractedAt = extractedAt
            },
            Meta = new ApiMetaDto
            {
                Model = _config["GEMINI_MODEL"] ?? "gemini-2.5-flash",
                DurationMs = sw.ElapsedMilliseconds
            }
        };
    }
}
