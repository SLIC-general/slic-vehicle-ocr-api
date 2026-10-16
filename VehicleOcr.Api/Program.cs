using DotNetEnv;
using VehicleOcr.Api.Config;
using VehicleOcr.Api.Middleware;
using VehicleOcr.Api.Services;

// Load .env before building configuration
Env.TraversePath().Load();

var builder = WebApplication.CreateBuilder(args);

// Add environment variables to configuration
builder.Configuration.AddEnvironmentVariables();

// Suppress HttpClient Info logs to prevent logging request URLs
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);

// Bind QualityCheckOptions
var qualityOptions = new QualityCheckOptions
{
    MinWidth = int.TryParse(Environment.GetEnvironmentVariable("MIN_WIDTH"), out var mw) ? mw : 800,
    MinHeight = int.TryParse(Environment.GetEnvironmentVariable("MIN_HEIGHT"), out var mh) ? mh : 600,
    MinBlurScore = double.TryParse(Environment.GetEnvironmentVariable("MIN_BLUR_SCORE"), out var mbs) ? mbs : 100.0,
    MinBrightness = double.TryParse(Environment.GetEnvironmentVariable("MIN_BRIGHTNESS"), out var mb) ? mb : 40.0,
    MaxBrightness = double.TryParse(Environment.GetEnvironmentVariable("MAX_BRIGHTNESS"), out var maxb) ? maxb : 220.0,
    MinContrast = double.TryParse(Environment.GetEnvironmentVariable("MIN_CONTRAST"), out var mc) ? mc : 20.0,
    MinQualityScore = double.TryParse(Environment.GetEnvironmentVariable("MIN_QUALITY_SCORE"), out var mqs) ? mqs : 0.40,
    LowFieldConfidence = double.TryParse(Environment.GetEnvironmentVariable("LOW_FIELD_CONFIDENCE"), out var lfc) ? lfc : 0.60
};
builder.Services.AddSingleton(qualityOptions);

// Bind GeminiOptions
var geminiOptions = new GeminiOptions
{
    ApiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? string.Empty,
    Model = Environment.GetEnvironmentVariable("GEMINI_MODEL") ?? "gemini-2.5-flash",
    TimeoutSeconds = int.TryParse(Environment.GetEnvironmentVariable("GEMINI_TIMEOUT_SECONDS"), out var gts) ? gts : 45,
    TotalTimeoutSeconds = int.TryParse(Environment.GetEnvironmentVariable("GEMINI_TOTAL_TIMEOUT_SECONDS"), out var gtts) ? gtts : 90,
    MaxAttempts = int.TryParse(Environment.GetEnvironmentVariable("GEMINI_MAX_ATTEMPTS"), out var gma) ? gma : 3,
    InitialBackoffMs = int.TryParse(Environment.GetEnvironmentVariable("GEMINI_INITIAL_BACKOFF_MS"), out var gib) ? gib : 1000,
    MaxBackoffMs = int.TryParse(Environment.GetEnvironmentVariable("GEMINI_MAX_BACKOFF_MS"), out var gmb) ? gmb : 10000,
    FallbackModel = Environment.GetEnvironmentVariable("GEMINI_FALLBACK_MODEL")
};
builder.Services.AddSingleton(geminiOptions);
builder.Services.AddSingleton<IDelayProvider, DefaultDelayProvider>();

// Add services
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var allowedOrigins =
    builder.Configuration
        .GetSection("Cors:AllowedOrigins")
        .Get<string[]>() ?? Array.Empty<string>();

// CORS for Frontend
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// App services
builder.Services.AddHttpClient<IGeminiClient, GeminiClient>(client =>
{
    client.Timeout = Timeout.InfiniteTimeSpan;
});

builder.Services.AddSingleton<IQualityCheckService, QualityCheckService>();
builder.Services.AddScoped<IOcrService, OcrService>();

var app = builder.Build();

app.UseMiddleware<GlobalExceptionHandler>();

if (app.Environment.IsDevelopment() || true)
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Vehicle CR Smart OCR API v1");
        c.RoutePrefix = "swagger";
    });
}

app.UseCors("AllowFrontend");

app.UseAuthorization();
app.MapControllers();

app.Run();
