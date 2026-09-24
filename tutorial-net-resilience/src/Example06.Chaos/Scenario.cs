using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Resilience.Shared;

namespace Example06.Chaos;

public static class Scenario
{
    public static async Task RunAsync(IServiceProvider services, CancellationToken ct)
    {
        var client = services.GetRequiredService<FlakyApiClientFactory>().Create(ResilienceConfig.ClientName);
        var monitor = services.GetRequiredService<IOptionsMonitor<ChaosOptions>>();
        var environment = services.GetRequiredService<IHostEnvironment>();
        var configuration = services.GetRequiredService<IConfiguration>();

        if (environment.IsProduction() || !AppSettingsFile.CanEditValue(configuration, "Chaos:Enabled"))
        {
            using var step = DemoNarrator.Step("Run with the effective chaos settings",
                "Production disables injection; environment, command-line and environment-specific file overrides are respected.");
            DemoNarrator.Info($"Environment = {environment.EnvironmentName}; Chaos:Enabled = {monitor.CurrentValue.Enabled}");
            DemoNarrator.Note("Skipping automatic file toggles because Production or a configuration override controls chaos.");
            await RunBatchAsync(client, ct);
            return;
        }

        var originalJson = AppSettingsFile.ReadAll();

        try
        {
            // Ensure the baseline is actually off even if a previous manual edit enabled chaos.
            AppSettingsFile.SetValue("Chaos:Enabled", JsonValue.Create(false));
            await AppSettingsFile.WaitForOptionsAsync(monitor, null, o => !o.Enabled, TimeSpan.FromSeconds(15));

            using (DemoNarrator.Step("Chaos off",
                       "Baseline: the pipeline is there but nothing is injected. 10 calls to /ok, all fast."))
            {
                DemoNarrator.Info($"Chaos:Enabled = {monitor.CurrentValue.Enabled}");
                await RunBatchAsync(client, ct);
            }

            using (DemoNarrator.Step("Chaos on: 30 % latency, 20 % faults, 30 % fake 500s",
                       "Turn chaos on from configuration and watch retry + timeout absorb the injected failures. The caller should still get (almost) all 200s: a call fails only when 4 attempts in a row are hit."))
            {
                AppSettingsFile.SetValue("Chaos:Enabled", JsonValue.Create(true));
                var reloaded = await AppSettingsFile.WaitForOptionsAsync(monitor, null, o => o.Enabled, TimeSpan.FromSeconds(15));
                DemoNarrator.Ok($"Chaos:Enabled = {reloaded.Enabled} (latency {reloaded.LatencyInjectionRate:P0}, faults {reloaded.FaultInjectionRate:P0}, outcomes {reloaded.OutcomeInjectionRate:P0})");
                await RunBatchAsync(client, ct);
                DemoNarrator.Note("Metrics: resilience.polly.strategy.events with event.name Chaos.OnLatency / Chaos.OnFault / Chaos.OnOutcome, plus OnRetry and OnTimeout.");
            }

            using (DemoNarrator.Step("Disable chaos again",
                       "The Development demo restores normal operation. In Production, post-configuration always forces injection off."))
            {
                AppSettingsFile.SetValue("Chaos:Enabled", JsonValue.Create(false));
                await AppSettingsFile.WaitForOptionsAsync(monitor, null, o => !o.Enabled, TimeSpan.FromSeconds(15));
                DemoNarrator.Info("Chaos disabled again — 3 calls to confirm:");
                for (var i = 1; i <= 3; i++)
                {
                    await DemoNarrator.CallAsync(() => client.OkAsync(ct));
                }
            }
        }
        finally
        {
            AppSettingsFile.WriteAll(originalJson);
        }
    }

    private static async Task RunBatchAsync(FlakyApiClient client, CancellationToken ct)
    {
        var successes = 0;
        for (var i = 1; i <= 10; i++)
        {
            DemoNarrator.Info($"call {i}");
            var result = await DemoNarrator.CallAsync(() => client.OkAsync(ct));
            if (result?.IsSuccess == true)
            {
                successes++;
            }
        }

        DemoNarrator.Ok($"{successes}/10 calls succeeded from the caller's point of view.");
    }
}
