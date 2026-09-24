using Microsoft.Extensions.DependencyInjection;
using Resilience.Shared;

namespace Example02.CustomPipeline;

public static class Scenario
{
    public static async Task RunAsync(IServiceProvider services, CancellationToken ct)
    {
        var client = services.GetRequiredService<FlakyApiClientFactory>().Create(ResilienceConfig.ClientName);

        using (DemoNarrator.Step("Timeout → retry → fallback",
                   "A slow dependency must not block the caller forever: bound each attempt, retry a little, then degrade gracefully."))
        {
            DemoNarrator.Info("Calling /slow?ms=5000 with a 1 s attempt timeout and 2 retries: expect 3 timeouts, then the fallback.");
            await DemoNarrator.CallAsync(() => client.SlowAsync(5000, ct));
            DemoNarrator.Info($"Circuit state: {ResilienceConfig.CircuitState.CircuitState}");
        }

        using (DemoNarrator.Step("Circuit breaker opens",
                   "When a dependency is down, keep retrying only makes it worse. After enough failures the breaker fails fast without touching the network."))
        {
            DemoNarrator.Info("Calling /unstable?failRate=1 six times (min throughput 5, failure ratio 50 %, sampling window 10 s).");
            DemoNarrator.Info("The 3 timeouts of step 1 are still inside the sampling window, so the breaker opens during call 1.");
            for (var i = 1; i <= 6; i++)
            {
                DemoNarrator.Info($"call {i}, circuit is {ResilienceConfig.CircuitState.CircuitState}");
                await DemoNarrator.CallAsync(() => client.UnstableAsync(failRate: 1.0, ct));
            }

            DemoNarrator.Note("Once OPEN, calls fail in ~0 ms with BrokenCircuitException: no retries, no HTTP spans.");
        }

        using (DemoNarrator.Step("Circuit breaker recovers",
                   "After BreakDuration the breaker goes half-open and lets one call through; success closes it."))
        {
            DemoNarrator.Info("Waiting 5 s for the break to expire…");
            await Task.Delay(TimeSpan.FromSeconds(5.2), ct);
            DemoNarrator.Info($"Circuit state before the trial call: {ResilienceConfig.CircuitState.CircuitState}");
            await DemoNarrator.CallAsync(() => client.OkAsync(ct));
            DemoNarrator.Info($"Circuit state after: {ResilienceConfig.CircuitState.CircuitState}");
        }

        using (DemoNarrator.Step("Manual control",
                   "A breaker can be forced open (kill switch during maintenance) and closed again from code or an admin endpoint."))
        {
            await ResilienceConfig.CircuitControl.IsolateAsync(ct);
            DemoNarrator.Info($"Isolated → {ResilienceConfig.CircuitState.CircuitState}. Calling /ok:");
            await DemoNarrator.CallAsync(() => client.OkAsync(ct));

            await ResilienceConfig.CircuitControl.CloseAsync(ct);
            DemoNarrator.Info($"Closed → {ResilienceConfig.CircuitState.CircuitState}. Calling /ok:");
            await DemoNarrator.CallAsync(() => client.OkAsync(ct));
        }
    }
}
