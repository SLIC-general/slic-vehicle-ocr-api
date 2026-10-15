using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using VehicleOcr.Api.Config;
using VehicleOcr.Api.Exceptions;
using VehicleOcr.Api.Services;

namespace VehicleOcr.Api.Tests;

public class GeminiClientTests
{
    private const string ValidGeminiJsonResponse = @"{
  ""candidates"": [
    {
      ""content"": {
        ""parts"": [
          {
            ""text"": ""{\""fields\"": {\""registrationNumber\"": \""WP CA-1234\"", \""makersName\"": \""TOYOTA\""}, \""confidenceScore\"": 0.95, \""fieldConfidence\"": {\""registrationNumber\"": 0.95}}""
          }
        ]
      }
    }
  ]
}";

    private static readonly byte[] SampleFileBytes = Encoding.UTF8.GetBytes("fake-image-bytes");
    private const string SampleMimeType = "image/png";

    private class TestDelayProvider : IDelayProvider
    {
        public List<TimeSpan> RecordedDelays { get; } = new();
        public Action<TimeSpan>? OnDelay { get; set; }

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default)
        {
            RecordedDelays.Add(delay);
            OnDelay?.Invoke(delay);
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled(cancellationToken);
            }
            return Task.CompletedTask;
        }
    }

    private class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handlerFunc;
        public List<HttpRequestMessage> Requests { get; } = new();
        public List<byte[]> RequestBodies { get; } = new();
        public int CallCount { get; private set; }

        public FakeHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handlerFunc)
        {
            _handlerFunc = handlerFunc;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            Requests.Add(request);
            if (request.Content != null)
            {
                var bytes = await request.Content.ReadAsByteArrayAsync(cancellationToken);
                RequestBodies.Add(bytes);
            }
            return await _handlerFunc(request, cancellationToken);
        }
    }

    private static GeminiOptions CreateTestOptions(
        int maxAttempts = 3,
        int initialBackoffMs = 1,
        int maxBackoffMs = 10,
        string? fallbackModel = null,
        int timeoutSeconds = 45,
        int totalTimeoutSeconds = 90)
    {
        return new GeminiOptions
        {
            ApiKey = "test-api-key-123456",
            Model = "gemini-test-primary",
            MaxAttempts = maxAttempts,
            InitialBackoffMs = initialBackoffMs,
            MaxBackoffMs = maxBackoffMs,
            FallbackModel = fallbackModel,
            TimeoutSeconds = timeoutSeconds,
            TotalTimeoutSeconds = totalTimeoutSeconds
        };
    }

    [Fact]
    public async Task Test1_TransientRetrySucceeds()
    {
        // Handler: 503, 503, 200 (valid response)
        // Assert: succeeds, handler called exactly 3 times
        int call = 0;
        var fakeHandler = new FakeHttpMessageHandler((req, ct) =>
        {
            call++;
            if (call < 3)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                {
                    Content = new StringContent("Service Unavailable")
                });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ValidGeminiJsonResponse, Encoding.UTF8, "application/json")
            });
        });

        var delayProvider = new TestDelayProvider();
        var client = new GeminiClient(new HttpClient(fakeHandler), CreateTestOptions(), NullLogger<GeminiClient>.Instance, delayProvider);

        var result = await client.GenerateCrDataAsync(SampleFileBytes, SampleMimeType);

        Assert.NotNull(result);
        Assert.Equal("WP CA-1234", result.Data.RegistrationNumber);
        Assert.Equal(3, fakeHandler.CallCount);
        Assert.Equal(2, delayProvider.RecordedDelays.Count);
    }

    [Fact]
    public async Task Test2_PermanentErrorIsNotRetried()
    {
        // Handler: 400
        // Assert: throws GeminiException, handler called exactly 1 time
        var fakeHandler = new FakeHttpMessageHandler((req, ct) =>
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("Bad Request: Invalid argument")
            });
        });

        var delayProvider = new TestDelayProvider();
        var client = new GeminiClient(new HttpClient(fakeHandler), CreateTestOptions(), NullLogger<GeminiClient>.Instance, delayProvider);

        var ex = await Assert.ThrowsAsync<GeminiException>(() => client.GenerateCrDataAsync(SampleFileBytes, SampleMimeType));

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal(1, fakeHandler.CallCount);
        Assert.Empty(delayProvider.RecordedDelays);
    }

    [Fact]
    public async Task Test3_AllRetriesFail_NoFallback()
    {
        // Handler: always 503, fallback not configured
        // Assert: throws GeminiException with status 503
        // Assert: handler called exactly MAX_ATTEMPTS times (3)
        var fakeHandler = new FakeHttpMessageHandler((req, ct) =>
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("Service Unavailable")
            });
        });

        var delayProvider = new TestDelayProvider();
        var client = new GeminiClient(new HttpClient(fakeHandler), CreateTestOptions(maxAttempts: 3, fallbackModel: null), NullLogger<GeminiClient>.Instance, delayProvider);

        var ex = await Assert.ThrowsAsync<GeminiException>(() => client.GenerateCrDataAsync(SampleFileBytes, SampleMimeType));

        Assert.Equal(503, ex.StatusCode);
        Assert.Equal(3, fakeHandler.CallCount);
        Assert.Equal(2, delayProvider.RecordedDelays.Count);
    }

    [Fact]
    public async Task Test4_FallbackSucceedsAfterPrimaryFails()
    {
        // Handler: primary always 503, fallback returns 200
        // Assert: succeeds, handler called MAX_ATTEMPTS + 1 times (4)
        var fakeHandler = new FakeHttpMessageHandler((req, ct) =>
        {
            var url = req.RequestUri?.ToString() ?? string.Empty;
            if (url.Contains("gemini-test-fallback"))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(ValidGeminiJsonResponse, Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("Primary Unavailable")
            });
        });

        var delayProvider = new TestDelayProvider();
        var options = CreateTestOptions(maxAttempts: 3, fallbackModel: "gemini-test-fallback");
        var client = new GeminiClient(new HttpClient(fakeHandler), options, NullLogger<GeminiClient>.Instance, delayProvider);

        var result = await client.GenerateCrDataAsync(SampleFileBytes, SampleMimeType);

        Assert.NotNull(result);
        Assert.Equal("WP CA-1234", result.Data.RegistrationNumber);
        Assert.Equal(4, fakeHandler.CallCount);
    }

    [Fact]
    public async Task Test5_CallerCancellationStopsRetries()
    {
        // Caller cancels after the first failure
        // Assert: no further handler calls
        using var callerCts = new CancellationTokenSource();
        var delayProvider = new TestDelayProvider
        {
            OnDelay = _ => callerCts.Cancel()
        };

        var fakeHandler = new FakeHttpMessageHandler((req, ct) =>
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("Service Unavailable")
            });
        });

        var client = new GeminiClient(new HttpClient(fakeHandler), CreateTestOptions(maxAttempts: 3), NullLogger<GeminiClient>.Instance, delayProvider);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GenerateCrDataAsync(SampleFileBytes, SampleMimeType, callerCts.Token));

        Assert.Equal(1, fakeHandler.CallCount);
    }

    [Fact]
    public async Task Test6_RetryAfterIsRespected()
    {
        // Handler: 429 with Retry-After: 1, then 200
        // Assert: waits ~1 second (recorded delay = 1 second without real wait), then succeeds
        int call = 0;
        var fakeHandler = new FakeHttpMessageHandler((req, ct) =>
        {
            call++;
            if (call == 1)
            {
                var resp = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                {
                    Content = new StringContent("Rate limited")
                };
                resp.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(1));
                return Task.FromResult(resp);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ValidGeminiJsonResponse, Encoding.UTF8, "application/json")
            });
        });

        var delayProvider = new TestDelayProvider();
        var client = new GeminiClient(new HttpClient(fakeHandler), CreateTestOptions(maxBackoffMs: 5000), NullLogger<GeminiClient>.Instance, delayProvider);

        var result = await client.GenerateCrDataAsync(SampleFileBytes, SampleMimeType);

        Assert.NotNull(result);
        Assert.Equal(2, fakeHandler.CallCount);
        Assert.Single(delayProvider.RecordedDelays);
        Assert.Equal(TimeSpan.FromSeconds(1), delayProvider.RecordedDelays[0]);
    }

    [Fact]
    public async Task Test7_PerAttemptTimeoutIsRetried()
    {
        // Handler: first call delays beyond timeout, second returns 200
        // Assert: succeeds with 2 calls
        int call = 0;
        var fakeHandler = new FakeHttpMessageHandler(async (req, ct) =>
        {
            call++;
            if (call == 1)
            {
                // Wait indefinitely until the per-attempt cancellation token triggers
                await Task.Delay(TimeSpan.FromSeconds(10), ct);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ValidGeminiJsonResponse, Encoding.UTF8, "application/json")
            };
        });

        var delayProvider = new TestDelayProvider();
        // Set a small per-attempt timeout of 1 second for test execution
        var options = CreateTestOptions(timeoutSeconds: 1);
        var client = new GeminiClient(new HttpClient(fakeHandler), options, NullLogger<GeminiClient>.Instance, delayProvider);

        var result = await client.GenerateCrDataAsync(SampleFileBytes, SampleMimeType);

        Assert.NotNull(result);
        Assert.Equal(2, fakeHandler.CallCount);
    }

    [Fact]
    public async Task Test8_RequestBodyIsReusableAcrossRetries()
    {
        // Handler: 503, then 200
        // Assert: second call's request body has the full byte content (not empty)
        int call = 0;
        var fakeHandler = new FakeHttpMessageHandler((req, ct) =>
        {
            call++;
            if (call == 1)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                {
                    Content = new StringContent("Service Unavailable")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ValidGeminiJsonResponse, Encoding.UTF8, "application/json")
            });
        });

        var delayProvider = new TestDelayProvider();
        var client = new GeminiClient(new HttpClient(fakeHandler), CreateTestOptions(), NullLogger<GeminiClient>.Instance, delayProvider);

        var result = await client.GenerateCrDataAsync(SampleFileBytes, SampleMimeType);

        Assert.NotNull(result);
        Assert.Equal(2, fakeHandler.CallCount);
        Assert.Equal(2, fakeHandler.RequestBodies.Count);
        Assert.True(fakeHandler.RequestBodies[0].Length > 0);
        Assert.True(fakeHandler.RequestBodies[1].Length > 0);
        Assert.Equal(fakeHandler.RequestBodies[0].Length, fakeHandler.RequestBodies[1].Length);
        Assert.Equal(fakeHandler.RequestBodies[0], fakeHandler.RequestBodies[1]);
    }
}
