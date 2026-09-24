using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.ExceptionSummarization;
using Polly.Telemetry;

namespace Resilience.Shared;

public static class ResilienceTelemetryExtensions
{
    /// <summary>
    /// 1. AddResilienceEnricher (Microsoft.Extensions.Resilience): enriches Polly's metrics with the
    ///    RequestMetadata stored in the ResilienceContext (request name, dependency name) and with a
    ///    compact error description produced by IExceptionSummarizer.
    /// 2. PollySpanEventsListener: bridges Polly events into span events (see that class).
    /// 3. PollyConsoleListener: compact console line per event (Polly's ILogger output is routed to OTLP only).
    /// </summary>
    public static IServiceCollection AddDemoResilienceTelemetry(this IServiceCollection services)
    {
        services.AddExceptionSummarizer(builder => builder.AddHttpProvider());
        services.AddResilienceEnricher();
        services.Configure<TelemetryOptions>(options =>
        {
            options.TelemetryListeners.Add(new PollySpanEventsListener());
            options.TelemetryListeners.Add(new PollyConsoleListener());
        });
        return services;
    }
}
