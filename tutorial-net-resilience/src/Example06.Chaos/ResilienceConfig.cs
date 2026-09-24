using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Simmy;
using Polly.Simmy.Fault;
using Polly.Simmy.Latency;
using Polly.Simmy.Outcomes;
using Resilience.Shared;

namespace Example06.Chaos;

/// <summary>
/// Chaos engineering, built into Polly (formerly "Simmy"): inject latency, exceptions and fake
/// results at a configurable rate to prove the resilience strategies actually work.
/// Chaos strategies go LAST (innermost) so that retry / circuit breaker / timeout above them see
/// the injected faults exactly as they would see real ones.
/// </summary>
public static class ResilienceConfig
{
    public const string ClientName = "chaos-demo";

    public static void Configure(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ChaosOptions>()
            .Bind(configuration.GetSection("Chaos"))
            .PostConfigure<IHostEnvironment>((options, environment) =>
            {
                // Enforced after binding on every reload, including environment/CLI overrides.
                if (environment.IsProduction())
                {
                    options.Enabled = false;
                }
            });

        services.AddHttpClient(ClientName, client => client.BaseAddress = new Uri(FlakyApiEndpoints.Primary))
            .AddResilienceHandler("chaos-pipeline", (builder, context) =>
            {
                // Read the knobs live: the generators below are evaluated on every execution, so
                // toggling "Chaos:Enabled" in appsettings.json takes effect without rebuilding the pipeline.
                var chaos = context.ServiceProvider.GetRequiredService<IOptionsMonitor<ChaosOptions>>();

                // --- the strategies under test -------------------------------------------------
                builder.AddRetry(new HttpRetryStrategyOptions
                {
                    Name = "retry",
                    MaxRetryAttempts = 3,
                    Delay = TimeSpan.FromMilliseconds(100),
                    BackoffType = DelayBackoffType.Constant,
                    OnRetry = args =>
                    {
                        DemoNarrator.Note($"retry #{args.AttemptNumber + 1} — {Describe(args.Outcome)}");
                        return default;
                    },
                });

                builder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
                {
                    Name = "circuit-breaker",
                    // Lenient on purpose: this demo wants to show retries absorbing chaos, not the breaker tripping.
                    MinimumThroughput = 50,
                    FailureRatio = 0.9,
                    SamplingDuration = TimeSpan.FromSeconds(30),
                    BreakDuration = TimeSpan.FromSeconds(5),
                });

                builder.AddTimeout(new HttpTimeoutStrategyOptions
                {
                    Name = "attempt-timeout",
                    Timeout = TimeSpan.FromSeconds(1),
                    OnTimeout = _ =>
                    {
                        DemoNarrator.Note("attempt timed out (1 s)");
                        return default;
                    },
                });

                // --- chaos, innermost ------------------------------------------------------------
                builder.AddChaosLatency(new ChaosLatencyStrategyOptions
                {
                    Name = "chaos-latency",
                    EnabledGenerator = _ => ValueTask.FromResult(chaos.CurrentValue.Enabled),
                    InjectionRateGenerator = _ => ValueTask.FromResult(chaos.CurrentValue.LatencyInjectionRate),
                    LatencyGenerator = _ => ValueTask.FromResult(TimeSpan.FromMilliseconds(chaos.CurrentValue.LatencyMs)),
                    OnLatencyInjected = args =>
                    {
                        DemoNarrator.Note($"chaos: +{args.Latency.TotalMilliseconds:N0} ms latency injected");
                        return default;
                    },
                });

                builder.AddChaosFault(new ChaosFaultStrategyOptions
                {
                    Name = "chaos-fault",
                    EnabledGenerator = _ => ValueTask.FromResult(chaos.CurrentValue.Enabled),
                    InjectionRateGenerator = _ => ValueTask.FromResult(chaos.CurrentValue.FaultInjectionRate),
                    FaultGenerator = new FaultGenerator().AddException(() => new HttpRequestException("chaos: connection reset by peer")),
                    OnFaultInjected = args =>
                    {
                        DemoNarrator.Note($"chaos: fault injected — {args.Fault.GetType().Name}");
                        return default;
                    },
                });

                builder.AddChaosOutcome(new ChaosOutcomeStrategyOptions<HttpResponseMessage>
                {
                    Name = "chaos-outcome",
                    EnabledGenerator = _ => ValueTask.FromResult(chaos.CurrentValue.Enabled),
                    InjectionRateGenerator = _ => ValueTask.FromResult(chaos.CurrentValue.OutcomeInjectionRate),
                    OutcomeGenerator = new OutcomeGenerator<HttpResponseMessage>()
                        .AddResult(() => new HttpResponseMessage(HttpStatusCode.InternalServerError) { ReasonPhrase = "chaos" }),
                    OnOutcomeInjected = args =>
                    {
                        DemoNarrator.Note($"chaos: outcome injected — {Describe(args.Outcome)}");
                        return default;
                    },
                });
            });
    }

    private static string Describe(Outcome<HttpResponseMessage> outcome)
        => outcome.Exception is { } ex ? ex.GetType().Name : $"HTTP {(int)outcome.Result!.StatusCode}";
}
