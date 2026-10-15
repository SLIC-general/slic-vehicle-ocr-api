using Docnet.Core;
using Docnet.Core.Converters;
using Docnet.Core.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using VehicleOcr.Api.Config;
using VehicleOcr.Api.Dtos;

namespace VehicleOcr.Api.Services;

public class QualityCheckService : IQualityCheckService
{
    private readonly QualityCheckOptions _options;
    private readonly ILogger<QualityCheckService> _logger;

    public QualityCheckService(QualityCheckOptions options, ILogger<QualityCheckService> logger)
    {
        _options = options;
        _logger = logger;
    }

    public Task<QualityResult> CheckAsync(byte[] fileBytes, string contentType)
    {
        if (fileBytes == null || fileBytes.Length == 0)
        {
            return Task.FromResult(new QualityResult
            {
                Score = 0.0,
                Grade = "UNUSABLE",
                NeedsReview = true,
                Reasons = new List<string> { "empty file" }
            });
        }

        try
        {
            Image<Rgba32> image;
            if (contentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase))
            {
                var scaling = 300.0 / 72.0;
                using var docReader = DocLib.Instance.GetDocReader(fileBytes, new PageDimensions(scaling));
                if (docReader.GetPageCount() == 0)
                {
                    return Task.FromResult(new QualityResult
                    {
                        Score = 0.0,
                        Grade = "UNUSABLE",
                        NeedsReview = true,
                        Reasons = new List<string> { "PDF has no pages" }
                    });
                }

                using var pageReader = docReader.GetPageReader(0);
                int width = pageReader.GetPageWidth();
                int height = pageReader.GetPageHeight();
                var rawBytes = pageReader.GetImage(new NaiveTransparencyRemover(255, 255, 255));

                using var bgra = Image.LoadPixelData<Bgra32>(rawBytes, width, height);
                image = bgra.CloneAs<Rgba32>();
            }
            else
            {
                image = Image.Load<Rgba32>(fileBytes);
            }

            using (image)
            {
                return Task.FromResult(EvaluateImage(image));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to analyze document quality");
            return Task.FromResult(new QualityResult
            {
                Score = 0.0,
                Grade = "UNUSABLE",
                NeedsReview = true,
                Reasons = new List<string> { $"corrupted or unreadable image: {ex.Message}" }
            });
        }
    }

    private QualityResult EvaluateImage(Image<Rgba32> image)
    {
        int w = image.Width;
        int h = image.Height;
        var reasons = new List<string>();

        // 1. Resolution: min 800x600 -> else penalise
        double resScore = 1.0;
        bool resOk = (w >= _options.MinWidth && h >= _options.MinHeight) || (w >= _options.MinHeight && h >= _options.MinWidth);
        if (!resOk)
        {
            double ratio = (double)(w * h) / (_options.MinWidth * _options.MinHeight);
            resScore = Math.Clamp(ratio, 0.0, 0.85);
            reasons.Add($"resolution={w}x{h} (below {_options.MinWidth}x{_options.MinHeight})");
        }

        // Grayscale conversion
        byte[] gray = new byte[w * h];
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                int offset = y * w;
                for (int x = 0; x < row.Length; x++)
                {
                    var p = row[x];
                    gray[offset + x] = (byte)(0.299f * p.R + 0.587f * p.G + 0.114f * p.B);
                }
            }
        });

        // 2. Brightness & Contrast
        double sum = 0;
        for (int i = 0; i < gray.Length; i++) sum += gray[i];
        double meanBright = sum / gray.Length;

        double sumSq = 0;
        for (int i = 0; i < gray.Length; i++)
        {
            double diff = gray[i] - meanBright;
            sumSq += diff * diff;
        }
        double contrastStdDev = Math.Sqrt(sumSq / gray.Length);

        // Brightness: 40-220 target, outside = penalise
        double brightScore = 1.0;
        if (meanBright < _options.MinBrightness)
        {
            brightScore = Math.Clamp(meanBright / _options.MinBrightness, 0.0, 0.85);
            reasons.Add($"brightness={meanBright:F1} (too dark)");
        }
        else if (meanBright > _options.MaxBrightness)
        {
            brightScore = Math.Clamp((255.0 - meanBright) / (255.0 - _options.MaxBrightness), 0.0, 0.85);
            reasons.Add($"brightness={meanBright:F1} (overexposed)");
        }

        // Contrast: std deviation, < 20 = faded
        double contrastScore = 1.0;
        if (contrastStdDev < _options.MinContrast)
        {
            contrastScore = Math.Clamp(contrastStdDev / _options.MinContrast, 0.0, 0.85);
            reasons.Add($"contrast={contrastStdDev:F1} (faded)");
        }

        // 3. Blur: Laplacian variance, < 100 = blurry
        double lapSum = 0;
        double lapSumSq = 0;
        long count = 0;
        for (int y = 1; y < h - 1; y++)
        {
            int rPrev = (y - 1) * w;
            int rCurr = y * w;
            int rNext = (y + 1) * w;
            for (int x = 1; x < w - 1; x++)
            {
                int lap = gray[rPrev + x] + gray[rNext + x] + gray[rCurr + x - 1] + gray[rCurr + x + 1] - 4 * gray[rCurr + x];
                lapSum += lap;
                lapSumSq += (double)lap * lap;
                count++;
            }
        }
        double lapMean = count > 0 ? lapSum / count : 0;
        double blurVar = count > 0 ? (lapSumSq / count) - (lapMean * lapMean) : 0;

        double blurScore = 1.0;
        if (blurVar < _options.MinBlurScore)
        {
            blurScore = Math.Clamp(blurVar / _options.MinBlurScore, 0.0, 0.85);
            string blurTag = blurVar < 40 ? "poor" : "fair";
            reasons.Add($"blur_score={blurVar:F1} ({blurTag})");
        }
        else
        {
            reasons.Add($"blur_score={Math.Round(blurVar)}");
        }

        // Average the four normalised scores
        double totalScore = Math.Round((resScore + brightScore + contrastScore + blurScore) / 4.0, 2);

        string grade;
        bool needsReview;
        if (totalScore >= 0.85)
        {
            grade = "GOOD";
            needsReview = false;
        }
        else if (totalScore >= 0.60)
        {
            grade = "FAIR";
            needsReview = true;
        }
        else if (totalScore >= _options.MinQualityScore)
        {
            grade = "POOR";
            needsReview = true;
        }
        else
        {
            grade = "UNUSABLE";
            needsReview = true;
        }

        return new QualityResult
        {
            Score = totalScore,
            Grade = grade,
            NeedsReview = needsReview,
            Reasons = reasons
        };
    }
}
