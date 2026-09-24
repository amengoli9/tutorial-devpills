using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace Resilience.Shared;

/// <summary>Common bootstrap for every example: host, logging, OpenTelemetry, resilience telemetry, FlakyApi clients.</summary>
public static class DemoHost
{
    /// <summary>Named HttpClient with NO resilience handler: used for the reachability check and for "before" comparisons.</summary>
    public const string RawClientName = "raw";

    public static HostApplicationBuilder CreateBuilder(string[] args, string exampleName)
    {
        // Content root = the folder of the executable, so appsettings.json is found no matter which
        // directory the reader launches "dotnet run" from (and AppSettingsFile edits the same file).
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
        });

        // Keep the console readable: single-line logs with a timestamp. Log levels come from appsettings.json.
        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(options =>
        {
            options.SingleLine = true;
            options.TimestampFormat = "HH:mm:ss.fff ";
        });
        // Polly's log records include full stack traces; on the console PollyConsoleListener shows
        // the same events in one line each. The OTLP logger still receives them (level from appsettings).
        builder.Logging.AddFilter<ConsoleLoggerProvider>("Polly", LogLevel.None);

        builder.AddDemoOpenTelemetry(exampleName);
        builder.Services.AddDemoResilienceTelemetry();
        builder.Services.AddSingleton<FlakyApiClientFactory>();
        // Named-client configuration runs after global defaults, even when those are registered later.
#pragma warning disable EXTEXP0001 // Explicit opt-out from global resilience defaults.
        builder.Services.AddHttpClient(RawClientName, client =>
        {
            client.BaseAddress = new Uri(FlakyApiEndpoints.Primary);
            client.Timeout = TimeSpan.FromSeconds(2);
        }).RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001

        DemoNarrator.ExampleName = exampleName;
        return builder;
    }

    public static async Task<int> RunAsync(
        HostApplicationBuilder builder,
        string description,
        Func<IServiceProvider, CancellationToken, Task> scenario)
    {
        DemoNarrator.Title(DemoNarrator.ExampleName, description);

        using var host = builder.Build();
        await host.StartAsync();

        var stopping = host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping;
        var exitCode = 0;
        try
        {
            var ping = host.Services.GetRequiredService<FlakyApiClientFactory>().Create(RawClientName);
            if (!await ping.IsReachableAsync(stopping))
            {
                DemoNarrator.Fail($"FlakyApi is not reachable at {FlakyApiEndpoints.Primary}. Start it first: dotnet run --project src/FlakyApi");
                return 2;
            }

            await scenario(host.Services, stopping);
            Console.WriteLine();
            DemoNarrator.Ok("Scenario complete.");
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            DemoNarrator.Note("Cancelled.");
        }
        catch (Exception ex)
        {
            DemoNarrator.Fail($"Unexpected error: {ex}");
            exitCode = 1;
        }
        finally
        {
            DemoNarrator.Note("Stopping host and flushing telemetry…");
            await host.StopAsync();
        }

        return exitCode;
    }
}
