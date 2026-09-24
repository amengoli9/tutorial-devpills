using System.Threading.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.Retry;
using Polly.Timeout;
using Resilience.Shared;

namespace Example05.NonHttpPipelines;

/// <summary>
/// Resilience is not only for HttpClient. Any operation (database, queue, cache, gRPC) can run
/// inside a ResiliencePipeline. Pipelines are registered by key in DI and resolved through
/// ResiliencePipelineProvider&lt;string&gt;; telemetry is enabled automatically.
/// </summary>
public static class ResilienceConfig
{
    public const string SavePipeline = "orders-db-save";
    public const string ReadPipeline = "orders-db-read";
    public const string BulkheadPipeline = "orders-db-bulkhead";
    public const string ThrottlePipeline = "orders-db-throttle";

    public static void Configure(IServiceCollection services)
    {
        services.AddSingleton<FakeOrdersRepository>();

        // Non-generic pipeline (the result does not matter): retry on transient exceptions + per-attempt timeout.
        services.AddResiliencePipeline(SavePipeline, builder =>
        {
            builder
                .AddRetry(new RetryStrategyOptions
                {
                    Name = "db-retry",
                    ShouldHandle = new PredicateBuilder().Handle<TimeoutException>().Handle<TimeoutRejectedException>(),
                    MaxRetryAttempts = 3,
                    Delay = TimeSpan.FromMilliseconds(100),
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    OnRetry = args =>
                    {
                        DemoNarrator.Note($"retry #{args.AttemptNumber + 1} after {args.Outcome.Exception?.GetType().Name}");
                        return default;
                    },
                })
                .AddTimeout(TimeSpan.FromSeconds(2));
        });

        // Generic pipeline: the RESULT decides. A null read (replica lag) is retried like a failure.
        services.AddResiliencePipeline<string, Order?>(ReadPipeline, builder =>
        {
            builder.AddRetry(new RetryStrategyOptions<Order?>
            {
                Name = "db-read-retry",
                ShouldHandle = new PredicateBuilder<Order?>().HandleResult(order => order is null).Handle<TimeoutException>(),
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromMilliseconds(100),
                OnRetry = args =>
                {
                    DemoNarrator.Note($"retry #{args.AttemptNumber + 1} because the result was {(args.Outcome.Result is null ? "null" : "an exception")}");
                    return default;
                },
            });
        });

        // Bulkhead: at most 2 executions at a time, no queue. Protects the database (and the rest of
        // this process) from a burst that would exhaust connections or threads.
        services.AddResiliencePipeline(BulkheadPipeline, builder =>
        {
            builder.AddConcurrencyLimiter(permitLimit: 2, queueLimit: 0);
        });

        // Throttle: 3 executions per second. Any System.Threading.RateLimiting limiter can be plugged in.
        services.AddResiliencePipeline(ThrottlePipeline, builder =>
        {
            builder.AddRateLimiter(new SlidingWindowRateLimiter(new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 3,
                Window = TimeSpan.FromSeconds(1),
                SegmentsPerWindow = 1,
                QueueLimit = 0,
            }));
        });
    }
}
