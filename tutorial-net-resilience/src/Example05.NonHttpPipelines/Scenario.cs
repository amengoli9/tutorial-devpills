using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Diagnostics;
using Microsoft.Extensions.Resilience;
using Polly;
using Polly.RateLimiting;
using Polly.Registry;
using Resilience.Shared;

namespace Example05.NonHttpPipelines;

public static class Scenario
{
    public static async Task RunAsync(IServiceProvider services, CancellationToken ct)
    {
        var repository = services.GetRequiredService<FakeOrdersRepository>();
        var pipelines = services.GetRequiredService<ResiliencePipelineProvider<string>>();

        using (DemoNarrator.Step("Retry a database write",
                   "The same retry/timeout strategies used for HTTP wrap any delegate. RequestMetadata on the context tags the metrics with the dependency name."))
        {
            var save = pipelines.GetPipeline(ResilienceConfig.SavePipeline);
            repository.ArmSaveFailures(2);

            // ResilienceContext carries the operation key (metrics tag operation.key), the
            // cancellation token and — via Microsoft.Extensions.Resilience — request metadata.
            var context = ResilienceContextPool.Shared.Get("SaveOrder", ct);
            context.SetRequestMetadata(new RequestMetadata
            {
                RequestName = "SaveOrder",
                DependencyName = "OrdersDb",
                MethodType = "DB",
                RequestRoute = "orders/{id}",
            });

            try
            {
                await save.ExecuteAsync(
                    static async (ctx, state) => await state.Repository.SaveAsync(state.Order, ctx.CancellationToken),
                    context,
                    (Repository: repository, Order: new Order(1, "ACME", 10m)));
                DemoNarrator.Ok("Order saved after two transient failures.");
            }
            finally
            {
                ResilienceContextPool.Shared.Return(context);
            }
        }

        using (DemoNarrator.Step("Retry on a *result*, not an exception",
                   "Generic pipelines (ResiliencePipeline<T>) can inspect the returned value: here null means 'not replicated yet, ask again'."))
        {
            var read = pipelines.GetPipeline<Order?>(ResilienceConfig.ReadPipeline);
            repository.ArmReadMisses(2);
            var order = await read.ExecuteAsync(async token => await repository.GetAsync(7, token), ct);
            DemoNarrator.Ok($"Read result: {order}");
        }

        using (DemoNarrator.Step("Bulkhead: limit concurrency",
                   "6 callers hit a slow query at once; only 2 may run, the other 4 are rejected immediately instead of piling up."))
        {
            var bulkhead = pipelines.GetPipeline(ResilienceConfig.BulkheadPipeline);
            var tasks = Enumerable.Range(1, 6).Select(async i =>
            {
                try
                {
                    var concurrent = await bulkhead.ExecuteAsync(async token => await repository.CountAsync(token), ct);
                    DemoNarrator.Ok($"caller {i}: ran with {concurrent} concurrent quer{(concurrent == 1 ? "y" : "ies")}");
                }
                catch (RateLimiterRejectedException)
                {
                    DemoNarrator.Fail($"caller {i}: rejected by the concurrency limiter");
                }
            });
            await Task.WhenAll(tasks);
        }

        using (DemoNarrator.Step("Throttle: 3 calls per second",
                   "Rate limiting on the caller side keeps you inside a quota (API keys, DB units) before the provider punishes you with 429s."))
        {
            var throttle = pipelines.GetPipeline(ResilienceConfig.ThrottlePipeline);
            for (var i = 1; i <= 5; i++)
            {
                try
                {
                    var order = await throttle.ExecuteAsync(async token => await repository.GetAsync(i, token), ct);
                    DemoNarrator.Ok($"call {i}: {order}");
                }
                catch (RateLimiterRejectedException ex)
                {
                    var hint = ex.RetryAfter is { } retryAfter ? $"retry after {retryAfter.TotalMilliseconds:N0} ms" : "this limiter gives no retry-after hint";
                    DemoNarrator.Fail($"call {i}: throttled ({hint})");
                }
            }
        }
    }
}
