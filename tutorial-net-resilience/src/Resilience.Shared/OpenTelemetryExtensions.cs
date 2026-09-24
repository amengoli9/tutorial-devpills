using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Resilience.Shared;

public static class OpenTelemetryExtensions
{
    /// <summary>
    /// Wires the OpenTelemetry SDK for traces, metrics and logs and exports everything over OTLP.
    /// The exporter reads the standard environment variables (OTEL_EXPORTER_OTLP_ENDPOINT,
    /// OTEL_EXPORTER_OTLP_PROTOCOL, OTEL_EXPORTER_OTLP_HEADERS) — nothing else is configured here,
    /// so the reader can point the demos at any OTLP-compatible backend.
    /// </summary>
    public static IHostApplicationBuilder AddDemoOpenTelemetry(
        this IHostApplicationBuilder builder,
        string serviceName,
        Action<TracerProviderBuilder>? configureTracing = null,
        Action<MeterProviderBuilder>? configureMetrics = null)
    {
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(
                serviceName: serviceName,
                serviceVersion: "1.0.0",
                serviceInstanceId: Environment.MachineName))
            .WithTracing(tracing =>
            {
                tracing
                    // Our own spans: one per demo step.
                    .AddSource(DemoTelemetry.ActivitySourceName)
                    // One span per HTTP attempt. Retries/hedges show up as sibling spans under the step.
                    .AddHttpClientInstrumentation()
                    .AddOtlpExporter();
                configureTracing?.Invoke(tracing);
            })
            .WithMetrics(metrics =>
            {
                metrics
                    // Polly emits resilience.polly.* instruments under this meter.
                    .AddMeter("Polly")
                    // Built-in HttpClient metrics (http.client.request.duration, ...).
                    .AddMeter("System.Net.Http")
                    .AddRuntimeInstrumentation()
                    // Demos live a few seconds: export every 5 s instead of the default 60 s.
                    .AddOtlpExporter((_, reader) =>
                        reader.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = 5_000);
                configureMetrics?.Invoke(metrics);
            })
            .WithLogging(logging => logging.AddOtlpExporter());

        // Log records carry scopes + the formatted message so Polly's "Resilience event occurred"
        // lines are readable in the backend.
        builder.Services.Configure<OpenTelemetryLoggerOptions>(options =>
        {
            options.IncludeScopes = true;
            options.IncludeFormattedMessage = true;
        });

        return builder;
    }
}
