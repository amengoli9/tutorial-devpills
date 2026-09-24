using Microsoft.Extensions.DependencyInjection;
using Resilience.Shared;

namespace Example03.Hedging;

public static class Scenario
{
    public static async Task RunAsync(IServiceProvider services, CancellationToken ct)
    {
        var clients = services.GetRequiredService<FlakyApiClientFactory>();

        using (DemoNarrator.Step("Baseline: slow region, no hedging",
                   "Region eu-east adds 2.5 s to every request. Without hedging the caller pays it in full."))
        {
            var client = clients.Create(ResilienceConfig.NoHedging);
            DemoNarrator.Info("GET /slow?ms=1500 on eu-east → expect ~4 s.");
            await DemoNarrator.CallAsync(() => client.SlowAsync(1500, ct));
        }

        using (DemoNarrator.Step("Ordered hedging: slow region first, fast region as the hedge",
                   "After 1 s without an answer a second attempt is fired at eu-west; whichever answers first wins, the other is cancelled."))
        {
            var client = clients.Create(ResilienceConfig.OrderedHedging);
            DemoNarrator.Info("GET /slow?ms=1500 → primary attempt on eu-east (4 s), hedge on eu-west after 1 s (1.5 s) → expect ~2.5 s from eu-west.");
            await DemoNarrator.CallAsync(() => client.SlowAsync(1500, ct));
            DemoNarrator.Note("Trace: two sibling HTTP spans under this step, the eu-east one ends with status 499 (cancelled). Span event polly.OnHedging marks the hedge.");
        }

        using (DemoNarrator.Step("Weighted hedging: 50/50 traffic split",
                   "Weighted groups route the first attempt by weight (A/B tests, canaries); the hedge still protects against a slow region."))
        {
            var client = clients.Create(ResilienceConfig.WeightedHedging);
            for (var i = 1; i <= 4; i++)
            {
                DemoNarrator.Info($"call {i}: GET /ok");
                await DemoNarrator.CallAsync(() => client.OkAsync(ct));
            }

            DemoNarrator.Note("Calls that started on eu-east were rescued by the hedge after 1 s; calls that started on eu-west answered immediately.");
            DemoNarrator.Note("Cost: hedging trades extra load on the dependency for lower tail latency. Only hedge idempotent requests.");
        }
    }
}
