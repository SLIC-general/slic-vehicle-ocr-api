using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using VehicleOcr.Api.Config;
using VehicleOcr.Api.Dtos;
using VehicleOcr.Api.Exceptions;

namespace VehicleOcr.Api.Services;

public class GeminiClient : IGeminiClient
{
    private readonly HttpClient _httpClient;
    private readonly GeminiOptions _options;
    private readonly ILogger<GeminiClient> _logger;
    private readonly IDelayProvider _delayProvider;

    private const string PromptText = @"
You are an expert OCR system specialized in extracting structured data from Sri Lankan Certificate of Registration (CR) vehicle documents.

TASK:
Examine the uploaded document and return a JSON object with:
1. ""fields"": Object containing all 28 vehicle registration fields:
   - registrationNumber
   - registrationDate
   - chassisNumber
   - engineNumber
   - ownerName
   - fatherHusbandName
   - currentAddress
   - permanentAddress
   - vehicleClass
   - makersName
   - modelName
   - bodyType
   - mfgMonthYear
   - noOfCylinders
   - cubicCapacity
   - horsePower
   - fuelType
   - seatingCapacity
   - standingCapacity
   - unladenWeight
   - grossWeight
   - color
   - wheelbase
   - taxValidity
   - fitnessValidity
   - insuranceDetails
   - hypothecation
   - issuingAuthority
2. ""confidenceScore"": Overall confidence score (0.0 to 1.0).
3. ""fieldConfidence"": Confidence score (0.0 to 1.0) for each of the 28 fields. For clear, unambiguous text assign 0.90 to 1.0. For degraded, blurry, or inferred text assign < 0.60. For empty/absent fields assign 0.0.

INSTRUCTIONS:
1. Extract Sinhala or Tamil script accurately where present.
2. If both Sinhala/Tamil and English appear, provide the English or standard transliterated value if clearer, or keep the original script.
3. If a value is missing or not present on the document, set that field value to empty string """" and confidence to 0.0.
4. Ensure technical identifiers (Chassis No, Engine No, Registration No) are captured exactly.

Return the final extraction as a JSON object matching this structure.";

    [ActivatorUtilitiesConstructor]
    public GeminiClient(
        HttpClient httpClient,
        ILogger<GeminiClient> logger,
        GeminiOptions options,
        IDelayProvider delayProvider)
    {
        _httpClient = httpClient;
        _logger = logger;
        _options = options;
        _delayProvider = delayProvider;
    }

    public GeminiClient(
        HttpClient httpClient,
        GeminiOptions options,
        ILogger<GeminiClient> logger,
        IDelayProvider? delayProvider = null)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
        _delayProvider = delayProvider ?? new DefaultDelayProvider();
    }

    public GeminiClient(
        HttpClient httpClient,
        IConfiguration config,
        ILogger<GeminiClient> logger)
        : this(httpClient, logger, new GeminiOptions
        {
            ApiKey = config["GEMINI_API_KEY"] ?? string.Empty,
            Model = config["GEMINI_MODEL"] ?? "gemini-2.5-flash",
            TimeoutSeconds = int.TryParse(config["GEMINI_TIMEOUT_SECONDS"], out var ts) ? ts : 45,
            TotalTimeoutSeconds = int.TryParse(config["GEMINI_TOTAL_TIMEOUT_SECONDS"], out var tts) ? tts : 90,
            MaxAttempts = int.TryParse(config["GEMINI_MAX_ATTEMPTS"], out var ma) ? ma : 3,
            InitialBackoffMs = int.TryParse(config["GEMINI_INITIAL_BACKOFF_MS"], out var ib) ? ib : 1000,
            MaxBackoffMs = int.TryParse(config["GEMINI_MAX_BACKOFF_MS"], out var mb) ? mb : 10000,
            FallbackModel = config["GEMINI_FALLBACK_MODEL"]
        }, new DefaultDelayProvider())
    {
    }

    public async Task<GeminiOcrResult> GenerateCrDataAsync(byte[] fileBytes, string mimeType, CancellationToken cancellationToken = default)
    {
        var apiKey = _options.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new GeminiException("GEMINI_API_KEY configuration is missing or empty.");
        }

        var base64Data = Convert.ToBase64String(fileBytes);
        var model = _options.Model;

        // Build the payload ONCE using the new helper method
        var jsonBytes = BuildPayload(model, mimeType, base64Data);

        var totalDeadline = DateTime.UtcNow.AddSeconds(_options.TotalTimeoutSeconds);
        var primaryModel = _options.Model;
        var maxAttempts = _options.MaxAttempts;

        int? lastPrimaryStatusCode = null;
        string? lastPrimaryErrorMessage = null;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            if (DateTime.UtcNow >= totalDeadline)
            {
                throw new GeminiException("Total deadline exceeded for Gemini API call.", lastPrimaryStatusCode ?? 504);
            }

            var endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/{primaryModel}:generateContent";

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Add("x-goog-api-key", apiKey);
            request.Content = new ByteArrayContent(jsonBytes);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attemptCts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

            HttpResponseMessage? response = null;
            bool isTransient = false;
            int statusCode = 0;
            string? errorMessage = null;
            TimeSpan? retryAfterDelay = null;

            try
            {
                response = await _httpClient.SendAsync(request, attemptCts.Token);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                isTransient = true;
                statusCode = 504;
                errorMessage = $"Request to Gemini API timed out after {_options.TimeoutSeconds} seconds.";
            }
            catch (HttpRequestException ex)
            {
                isTransient = true;
                statusCode = 503;
                errorMessage = $"Network error when communicating with Gemini API: {ex.Message}";
            }
            catch (Exception ex)
            {
                errorMessage = $"Unexpected error communicating with Gemini API: {ex.Message}";
            }

            if (response != null)
            {
                using (response)
                {
                    statusCode = (int)response.StatusCode;

                    if (response.IsSuccessStatusCode)
                    {
                        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                        return ParseResponse(responseBody);
                    }

                    if (IsTransientStatusCode(response.StatusCode))
                    {
                        isTransient = true;
                        errorMessage = $"Gemini API returned transient error (HTTP {statusCode}).";
                        retryAfterDelay = ParseRetryAfter(response);

                        // NEW: Read and log Google's exact error message
                        var transientBody = await response.Content.ReadAsStringAsync(cancellationToken);
                        _logger.LogWarning("Gemini transient {Status} from {Model}: {Body}",
                            statusCode, primaryModel,
                            transientBody.Length > 500 ? transientBody[..500] : transientBody);
                    }
                    else
                    {
                        var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                        _logger.LogError("Gemini API error [{Status}]", statusCode);
                        throw new GeminiException($"Gemini API error (HTTP {statusCode}): {errorBody}", statusCode);
                    }
                }
            }

            if (isTransient)
            {
                lastPrimaryStatusCode = statusCode;
                lastPrimaryErrorMessage = errorMessage;

                if (attempt < maxAttempts)
                {
                    TimeSpan delay;
                    if (retryAfterDelay.HasValue)
                    {
                        delay = retryAfterDelay.Value;
                    }
                    else
                    {
                        var backoffMs = _options.InitialBackoffMs * (int)Math.Pow(2, attempt - 1);
                        delay = TimeSpan.FromMilliseconds(backoffMs);
                    }

                    var maxBackoff = TimeSpan.FromMilliseconds(_options.MaxBackoffMs);
                    if (delay > maxBackoff) delay = maxBackoff;
                    if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;

                    var remainingTime = totalDeadline - DateTime.UtcNow;
                    if (remainingTime <= TimeSpan.Zero || delay > remainingTime)
                    {
                        throw new GeminiException("Total deadline exceeded for Gemini API call.", statusCode);
                    }

                    _logger.LogWarning("Gemini retry in {Delay}ms (attempt {Attempt}/{Max}, status {Code})", (int)delay.TotalMilliseconds, attempt, maxAttempts, statusCode);

                    try
                    {
                        await _delayProvider.DelayAsync(delay, cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                }
            }
            else if (errorMessage != null)
            {
                throw new GeminiException(errorMessage, statusCode > 0 ? statusCode : 500);
            }
        }

        // Fallback Loop
        if (!string.IsNullOrWhiteSpace(_options.FallbackModel))
        {
            var fallbackModel = _options.FallbackModel;

            if (DateTime.UtcNow >= totalDeadline)
            {
                throw new GeminiException("Total deadline exceeded before fallback attempt.", lastPrimaryStatusCode ?? 504);
            }

            _logger.LogWarning("Gemini falling back to {FallbackModel}", fallbackModel);

            var fallbackEndpoint = $"https://generativelanguage.googleapis.com/v1beta/models/{fallbackModel}:generateContent";

            // Build payload for fallback model
            var fbBytes = BuildPayload(fallbackModel, mimeType, base64Data);

            using var fbRequest = new HttpRequestMessage(HttpMethod.Post, fallbackEndpoint);
            fbRequest.Headers.Add("x-goog-api-key", apiKey);

            // Use the fallback payload bytes here
            fbRequest.Content = new ByteArrayContent(fbBytes);
            fbRequest.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            using var fbAttemptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            fbAttemptCts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

            try
            {
                using var fbResponse = await _httpClient.SendAsync(fbRequest, fbAttemptCts.Token);
                var fbStatusCode = (int)fbResponse.StatusCode;

                if (fbResponse.IsSuccessStatusCode)
                {
                    var fbBody = await fbResponse.Content.ReadAsStringAsync(cancellationToken);
                    return ParseResponse(fbBody);
                }

                if (!IsTransientStatusCode(fbResponse.StatusCode))
                {
                    var fbError = await fbResponse.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogWarning("Fallback model '{FallbackModel}' failed with permanent error (HTTP {Status}): {Error}", fallbackModel, fbStatusCode, fbError);
                    throw new GeminiException(lastPrimaryErrorMessage ?? $"Gemini service unavailable (HTTP {lastPrimaryStatusCode}).", lastPrimaryStatusCode ?? 503);
                }
                else
                {
                    throw new GeminiException($"Fallback model '{fallbackModel}' failed with transient error (HTTP {fbStatusCode}).", fbStatusCode);
                }
            }
            catch (GeminiException)
            {
                throw;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Fallback model '{FallbackModel}' call failed: {Message}", fallbackModel, ex.Message);
                throw new GeminiException(lastPrimaryErrorMessage ?? $"Gemini service unavailable (HTTP {lastPrimaryStatusCode}).", lastPrimaryStatusCode ?? 503);
            }
        }

        throw new GeminiException(lastPrimaryErrorMessage ?? $"Gemini service unavailable (HTTP {lastPrimaryStatusCode}).", lastPrimaryStatusCode ?? 503);
    }

    // The new Payload Builder Method
    private static byte[] BuildPayload(string model, string mimeType, string base64Data)
    {
        var generationConfig = new Dictionary<string, object>
        {
            ["responseMimeType"] = "application/json",
            ["temperature"] = 0,
            ["maxOutputTokens"] = 4096
        };

        if (model.Contains("gemini-3", StringComparison.OrdinalIgnoreCase))
            generationConfig["thinkingConfig"] = new { thinkingLevel = "low" };
        else if (model.Contains("2.5-flash", StringComparison.OrdinalIgnoreCase))
            generationConfig["thinkingConfig"] = new { thinkingBudget = 0 };

        var payload = new
        {
            contents = new[]
            {
                new
                {
                    parts = new object[]
                    {
                        new { inlineData = new { mimeType, data = base64Data } },
                        new { text = PromptText }
                    }
                }
            },
            generationConfig
        };
        return JsonSerializer.SerializeToUtf8Bytes(payload);
    }

    private static bool IsTransientStatusCode(HttpStatusCode statusCode)
    {
        int code = (int)statusCode;
        return code == 429 || code == 500 || code == 502 || code == 503 || code == 504;
    }

    private static TimeSpan? ParseRetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter != null)
        {
            if (response.Headers.RetryAfter.Delta.HasValue)
            {
                return response.Headers.RetryAfter.Delta.Value;
            }

            if (response.Headers.RetryAfter.Date.HasValue)
            {
                var diff = response.Headers.RetryAfter.Date.Value - DateTimeOffset.UtcNow;
                return diff > TimeSpan.Zero ? diff : TimeSpan.Zero;
            }
        }

        if (response.Headers.TryGetValues("Retry-After", out var values))
        {
            var raw = values.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(raw))
            {
                if (int.TryParse(raw, out var seconds))
                {
                    return TimeSpan.FromSeconds(seconds);
                }

                if (DateTimeOffset.TryParse(raw, out var parsedDate))
                {
                    var diff = parsedDate - DateTimeOffset.UtcNow;
                    return diff > TimeSpan.Zero ? diff : TimeSpan.Zero;
                }
            }
        }

        return null;
    }

    private GeminiOcrResult ParseResponse(string responseBody)
    {
        try
        {
            var jsonDoc = JsonNode.Parse(responseBody);
            var text = jsonDoc?["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.ToString();

            _logger.LogDebug("Gemini raw text output: {Text}", text);

            if (string.IsNullOrWhiteSpace(text))
            {
                throw new OcrFailedException("Gemini returned an empty extraction result.");
            }

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var parsedRoot = JsonNode.Parse(text);
            VehicleCrData? fields = null;
            var confidence = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

            if (parsedRoot?["fields"] != null)
            {
                fields = parsedRoot["fields"].Deserialize<VehicleCrData>(options);
            }
            else
            {
                fields = JsonSerializer.Deserialize<VehicleCrData>(text, options);
            }

            if (fields == null)
            {
                throw new OcrFailedException("Failed to deserialize structured OCR data from Gemini response.");
            }

            if (parsedRoot?["confidenceScore"] != null && double.TryParse(parsedRoot["confidenceScore"]!.ToString(), out var cs))
            {
                fields.ConfidenceScore = (decimal)cs;
            }

            if (parsedRoot?["fieldConfidence"] != null)
            {
                foreach (var prop in parsedRoot["fieldConfidence"]!.AsObject())
                {
                    if (prop.Value != null && decimal.TryParse(prop.Value.ToString(), out var score))
                    {
                        confidence[prop.Key] = Math.Clamp(score, 0.0m, 1.0m);
                    }
                }
            }

            return new GeminiOcrResult(fields, confidence);
        }
        catch (OcrFailedException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse Gemini response payload");
            throw new OcrFailedException($"Could not extract vehicle data from model response: {ex.Message}");
        }
    }
}