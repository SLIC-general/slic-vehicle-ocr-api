# Vehicle OCR API (.NET 8 Backend)

Stateless ASP.NET Core Web API handling document rasterization, image quality analysis, and Google Gemini Vision 28-field extraction. Storage is handled downstream by the insurance form system.

---

## ⚙️ Environment Variables (`.env`)

| Key | Default | Description |
|---|---|---|
| `GEMINI_API_KEY` | *Required* | Google AI Studio Gemini API Key. Never logged or exposed. |
| `GEMINI_MODEL` | `gemini-2.5-flash` | Gemini Vision model name. |
| `MAX_FILE_SIZE_MB` | `10` | Maximum allowed file upload size in megabytes. |
| `ALLOWED_ORIGINS` | `http://localhost:3000` | Allowed CORS origins. |
| `MIN_WIDTH` | `800` | Minimum acceptable document pixel width. |
| `MIN_HEIGHT` | `600` | Minimum acceptable document pixel height. |
| `MIN_BLUR_SCORE` | `100.0` | Minimum Laplacian variance before blur penalty. |
| `MIN_BRIGHTNESS` | `40` | Minimum acceptable average luminance. |
| `MAX_BRIGHTNESS` | `220` | Maximum acceptable average luminance. |
| `MIN_CONTRAST` | `20.0` | Minimum pixel standard deviation before faded penalty. |
| `MIN_QUALITY_SCORE` | `0.40` | Threshold below which image is deemed UNUSABLE. |
| `LOW_FIELD_CONFIDENCE` | `0.60` | Field confidence threshold below which fields are flagged. |

### Adding New Environment Keys
1. Add key-value pair to `backend/.env` and `backend/.env.example`.
2. Map to a typed options class in `VehicleOcr.Api/Config/` or inject `IConfiguration`.
3. Register in `Program.cs`.

---

## 🏗️ SOLID Architecture

- **Controllers**: Thin controller handling routing and HTTP requests (`OcrController`).
- **Services**:
  - `IQualityCheckService` / `QualityCheckService`: SixLabors.ImageSharp and Docnet.Core analysis.
  - `IGeminiClient` / `GeminiClient`: Resilient typed HttpClient with structured JSON prompts.
  - `IOcrService` / `OcrService`: Business orchestrator linking quality check and AI extraction.
- **Middleware**: `GlobalExceptionHandler` returning uniform REST error envelopes.
