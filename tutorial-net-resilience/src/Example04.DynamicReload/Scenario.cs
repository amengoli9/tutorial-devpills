using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Resilience.Shared;

namespace Example04.DynamicReload;

public static class Scenario
{
    public static async Task RunAsync(IServiceProvider services, CancellationToken ct)
    {
        var client = services.GetRequiredService<FlakyApiClientFactory>().Create(ResilienceConfig.ClientName);
        var monitor = services.GetRequiredService<IOptionsMonitor<HttpRetryStrategyOptions>>();
        var originalJson = AppSettingsFile.ReadAll();

        try
        {
            using (DemoNarrator.Step("Too few retries",
                       "appsettings.json says MaxRetryAttempts = 1. The API needs 4 calls to succeed, so this fails."))
            {
                DemoNarrator.Info($"Current options: MaxRetryAttempts = {monitor.Get(ResilienceConfig.RetryOptionsName).MaxRetryAttempts}");
                await DemoNarrator.CallAsync(() => client.FailThenOkAsync(failures: 3, key: Guid.NewGuid().ToString("N"), ct));
            }

            using (DemoNarrator.Step("Edit the file while running",
                       "No restart: the options monitor sees the change and Polly rebuilds the pipeline."))
            {
                DemoNarrator.Info($"Writing MaxRetryAttempts = 4 into {AppSettingsFile.Path}");
                AppSettingsFile.SetValue("Resilience:Retry:MaxRetryAttempts", JsonValue.Create(4));

                var reloaded = await AppSettingsFile.WaitForOptionsAsync(
                    monitor, ResilienceConfig.RetryOptionsName, o => o.MaxRetryAttempts == 4, TimeSpan.FromSeconds(15));
                DemoNarrator.Ok($"Options reloaded: MaxRetryAttempts = {reloaded.MaxRetryAttempts}");

                DemoNarrator.Info("Same call as before — now it succeeds on the 4th attempt.");
                await DemoNarrator.CallAsync(() => client.FailThenOkAsync(failures: 3, key: Guid.NewGuid().ToString("N"), ct));
            }
        }
        finally
        {
            AppSettingsFile.WriteAll(originalJson);
            DemoNarrator.Note("appsettings.json restored so the demo is repeatable.");
        }
    }
}
