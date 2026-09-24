using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.CircuitBreaker;
using Polly.Fallback;
using Polly.Timeout;
using Resilience.Shared;

namespace Example02.CustomPipeline;

/// <summary>
/// AddResilienceHandler lets you compose your own pipeline. Order matters: strategies are added
/// outermost → innermost, and a request flows through them in that order.
///   fallback → retry → circuit breaker → attempt timeout → (HttpClient)
/// So: a timeout is retried; repeated failures open the breaker; when everything fails the
/// fallback returns a degraded answer instead of an exception.
/// </summary>
public static class ResilienceConfig
{
    public const string ClientName = "custom-pipeline";

    /// <summary>Read the breaker state from anywhere (dashboards, health checks, this demo).</summary>
    public static readonly CircuitBreakerStateProvider CircuitState = new();

    /// <summary>Open/close the breaker by hand (maintenance windows, kill switch).</summary>
    public static readonly CircuitBreakerManualControl CircuitControl = new();

    public static void Configure(IServiceCollection services)
    {
        services.AddHttpClient(ClientName, client => client.BaseAddress = new Uri(FlakyApiEndpoints.Primary))
            .AddResilienceHandler("custom", builder =>
            {
                // 1. Fallback (outermost): only for timeouts, so the other failure modes stay visible in this demo.
                builder.AddFallback(new FallbackStrategyOptions<HttpResponseMessage>
                {
                    Name = "fallback",
                    ShouldHandle = new PredicateBuilder<HttpResponseMessage>().Handle<TimeoutRejectedException>(),
                    FallbackAction = _ =>
                    {
                        var cached = new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent("{\"message\":\"stale data served from cache\"}", Encoding.UTF8, "application/json"),
                        };
                        cached.Headers.Add("X-Region", "fallback-cache");
                        return Outcome.FromResultAsValueTask(cached);
                    },
                    OnFallback = args =>
                    {
                        DemoNarrator.Note($"fallback: serving cached data because of {Describe(args.Outcome)}");
                        return default;
                    },
                });

                // 2. Retry: 2 extra attempts, constant 200 ms. HttpRetryStrategyOptions already handles
                //    5xx/408/429, HttpRequestException and TimeoutRejectedException.
                builder.AddRetry(new HttpRetryStrategyOptions
                {
                    Name = "retry",
                    MaxRetryAttempts = 2,
                    Delay = TimeSpan.FromMilliseconds(200),
                    BackoffType = DelayBackoffType.Constant,
                    OnRetry = args =>
                    {
                        DemoNarrator.Note($"retry #{args.AttemptNumber + 1} in {args.RetryDelay.TotalMilliseconds:N0} ms because of {Describe(args.Outcome)}");
                        return default;
                    },
                });

                // 3. Circuit breaker: tuned small so the demo trips it with a handful of calls.
                //    Production defaults are 10 % over 30 s with at least 100 calls.
                builder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
                {
                    Name = "circuit-breaker",
                    SamplingDuration = TimeSpan.FromSeconds(10),
                    MinimumThroughput = 5,
                    FailureRatio = 0.5,
                    BreakDuration = TimeSpan.FromSeconds(5),
                    StateProvider = CircuitState,
                    ManualControl = CircuitControl,
                    OnOpened = args =>
                    {
                        var duration = args.IsManual ? "indefinitely (manual)" : $"for {args.BreakDuration.TotalSeconds:N0} s";
                        DemoNarrator.Note($"circuit OPENED {duration} — calls now fail fast");
                        return default;
                    },
                    OnHalfOpened = _ =>
                    {
                        DemoNarrator.Note("circuit HALF-OPEN — one trial call is allowed through");
                        return default;
                    },
                    OnClosed = args =>
                    {
                        DemoNarrator.Note($"circuit CLOSED (manual: {args.IsManual}) — back to normal");
                        return default;
                    },
                });

                // 4. Attempt timeout (innermost): bounds a single try; the retry above decides what to do next.
                builder.AddTimeout(new HttpTimeoutStrategyOptions
                {
                    Name = "attempt-timeout",
                    Timeout = TimeSpan.FromSeconds(1),
                    OnTimeout = args =>
                    {
                        DemoNarrator.Note($"attempt timed out after {args.Timeout.TotalSeconds:N1} s");
                        return default;
                    },
                });
            });
    }

    private static string Describe(Outcome<HttpResponseMessage> outcome)
        => outcome.Exception is { } ex ? ex.GetType().Name : $"HTTP {(int)outcome.Result!.StatusCode}";
}
