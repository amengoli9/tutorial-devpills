using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Resilience.Shared;

namespace Example04.DynamicReload;

/// <summary>
/// Resilience settings usually need tuning under pressure — exactly when you cannot redeploy.
/// Polly pipelines registered through DI can be rebuilt when their options change:
///   1. bind named options from configuration (appsettings.json is loaded with reloadOnChange),
///   2. call context.EnableReloads&lt;TOptions&gt;(name) inside AddResilienceHandler,
///   3. read the options through context.GetOptions and build the pipeline from them.
/// When the file changes, IOptionsMonitor fires and Polly rebuilds the pipeline with the new values.
/// </summary>
public static class ResilienceConfig
{
    public const string ClientName = "reloadable";
    public const string RetryOptionsName = "Retry";

    public static void Configure(IServiceCollection services, IConfiguration configuration)
    {
        // Named options bound to "Resilience:Retry". The delegate properties (ShouldHandle, OnRetry)
        // are not bindable and keep their defaults.
        services.Configure<HttpRetryStrategyOptions>(RetryOptionsName, configuration.GetSection("Resilience:Retry"));

        services.AddHttpClient(ClientName, client => client.BaseAddress = new Uri(FlakyApiEndpoints.Primary))
            .AddResilienceHandler("reloadable-pipeline", (builder, context) =>
            {
                // Rebuild this pipeline whenever the named options change.
                context.EnableReloads<HttpRetryStrategyOptions>(RetryOptionsName);

                var configured = context.GetOptions<HttpRetryStrategyOptions>(RetryOptionsName);

                // Copy the bound values into a fresh instance instead of mutating the cached options object.
                builder.AddRetry(new HttpRetryStrategyOptions
                {
                    Name = "retry",
                    MaxRetryAttempts = configured.MaxRetryAttempts,
                    Delay = configured.Delay,
                    BackoffType = configured.BackoffType,
                    UseJitter = configured.UseJitter,
                    OnRetry = args =>
                    {
                        DemoNarrator.Note($"retry #{args.AttemptNumber + 1} (max {configured.MaxRetryAttempts})");
                        return default;
                    },
                });

                DemoNarrator.Note($"pipeline built with MaxRetryAttempts = {configured.MaxRetryAttempts}, Delay = {configured.Delay.TotalMilliseconds:N0} ms");
            });
    }
}
