using Microsoft.Extensions.DependencyInjection;
using Resilience.Shared;

namespace Example01.StandardHandler;

public static class Scenario
{
    public static async Task RunAsync(IServiceProvider services, CancellationToken ct)
    {
        var clients = services.GetRequiredService<FlakyApiClientFactory>();

        using (DemoNarrator.Step("Standard handler, default options",
                   "One line of code gives retry + circuit breaker + timeouts + rate limiting with sane defaults."))
        {
            var client = clients.Create(ResilienceConfig.Defaults);
            DemoNarrator.Info("Calling /fail-then-ok?failures=2 — the API will answer 500, 500, then 200.");
            await DemoNarrator.CallAsync(() => client.FailThenOkAsync(failures: 2, key: NewKey(), ct));
            DemoNarrator.Note("The caller saw only the final 200. The two retries are visible as span events and in resilience.polly.strategy.events.");
        }

        using (DemoNarrator.Step("Same handler, options bound from appsettings.json",
                   "Retry count, delays and timeouts belong to configuration, not to code: ops tune them per environment."))
        {
            var client = clients.Create(ResilienceConfig.FromConfig);
            DemoNarrator.Info("Configured: 5 retries, 300 ms exponential backoff, 1 s attempt timeout. Calling /fail-then-ok?failures=4.");
            await DemoNarrator.CallAsync(() => client.FailThenOkAsync(failures: 4, key: NewKey(), ct));
            DemoNarrator.Info("Now /slow?ms=2000: every attempt exceeds the 1 s attempt timeout, so all 6 attempts time out.");
            await DemoNarrator.CallAsync(() => client.SlowAsync(2000, ct));
        }

        using (DemoNarrator.Step("Server-driven backoff with Retry-After",
                   "When the server says 'come back in N seconds', the retry strategy honors it (HttpRetryStrategyOptions.ShouldRetryAfterHeader)."))
        {
            var client = clients.Create(ResilienceConfig.Defaults);
            DemoNarrator.Info("Calling /status/429?retryAfter=1 — 3 retries spaced exactly 1 s apart instead of the exponential 2 s/4 s/8 s.");
            await DemoNarrator.CallAsync(() => client.StatusAsync(429, retryAfterSeconds: 1, ct));
        }

        using (DemoNarrator.Step("Do not retry unsafe methods",
                   "POST is not idempotent: retrying it can duplicate side effects. DisableForUnsafeHttpMethods() keeps retries for GET/HEAD/OPTIONS only."))
        {
            var client = clients.Create(ResilienceConfig.NoUnsafeRetries);
            DemoNarrator.Info("POST /orders always fails — expect a single attempt and an immediate 500.");
            await DemoNarrator.CallAsync(() => client.CreateOrderAsync(ct));
        }

        using (DemoNarrator.Step("Global default + explicit opt-out",
                   "ConfigureHttpClientDefaults applies the standard handler to every client; RemoveAllResilienceHandlers() opts one client out."))
        {
            var client = clients.Create(ResilienceConfig.Raw);
            DemoNarrator.Info("Raw client, /fail-then-ok?failures=2 — no retries, the first 500 surfaces immediately.");
            await DemoNarrator.CallAsync(() => client.FailThenOkAsync(failures: 2, key: NewKey(), ct));
        }
    }

    private static string NewKey() => Guid.NewGuid().ToString("N");
}
