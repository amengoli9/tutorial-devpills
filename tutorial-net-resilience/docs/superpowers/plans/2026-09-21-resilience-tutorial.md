# tutorial-net-resilience Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A DevPills tutorial solution showing the current Microsoft.Extensions.Resilience / Http.Resilience (10.10.0, Polly 8.8) feature set, one narrated console example per project, everything instrumented with OpenTelemetry and exported over OTLP only.

**Architecture:** `Resilience.Shared` owns OTel bootstrap, a Polly→span-event bridge and console narration; `FlakyApi` is a two-port minimal API that fails on purpose; `Example01…06` are console apps, each = `Program.cs` (bootstrap) + `ResilienceConfig.cs` (the feature) + `Scenario.cs` (narrated steps).

**Tech Stack:** .NET 10 SDK (10.0.200), Microsoft.Extensions.Hosting 10.0.12, Microsoft.Extensions.Resilience + Http.Resilience 10.10.0, Polly 8.8.0 (transitive), OpenTelemetry 1.19.x with OTLP exporter, ASP.NET Core minimal API.

**Spec:** `docs/superpowers/specs/2026-09-21-resilience-tutorial-design.md`

## Global Constraints

- Root folder: `D:\progetti\privale\repo_tutorial\tutorial-net-resilience`. All paths below are relative to it.
- `TargetFramework=net10.0`, `Nullable=enable`, `ImplicitUsings=enable`, `TreatWarningsAsErrors=true`, central package management.
- OTLP is the **only** exporter. No Aspire, no console/Seq exporters. Endpoint comes only from `OTEL_EXPORTER_OTLP_*` env vars.
- README in Italian; code comments in English.
- No test project; each task is verified by `dotnet build` and by running the scenario against FlakyApi and comparing the console output with the "Expected" block.
- No git operations (the user has not asked for a repo/commits).
- Verification runs use PowerShell from the root folder. FlakyApi must be running for tasks 3–9 (`dotnet run --project src/FlakyApi` in a separate background shell).

---

### Task 1: Solution scaffolding + `Resilience.Shared`

**Files:**
- Create: `Directory.Build.props`, `Directory.Packages.props`, `.gitignore`, `TutorialNetResilience.slnx`
- Create: `src/Resilience.Shared/Resilience.Shared.csproj`
- Create: `src/Resilience.Shared/DemoTelemetry.cs`
- Create: `src/Resilience.Shared/OpenTelemetryExtensions.cs`
- Create: `src/Resilience.Shared/PollySpanEventsListener.cs`
- Create: `src/Resilience.Shared/ResilienceTelemetryExtensions.cs`
- Create: `src/Resilience.Shared/DemoNarrator.cs`
- Create: `src/Resilience.Shared/FlakyApiEndpoints.cs`
- Create: `src/Resilience.Shared/ApiResult.cs`
- Create: `src/Resilience.Shared/FlakyApiClient.cs`
- Create: `src/Resilience.Shared/FlakyApiClientFactory.cs`
- Create: `src/Resilience.Shared/DemoHost.cs`
- Create: `src/Resilience.Shared/AppSettingsFile.cs`

**Interfaces (Produces):**
- `IHostApplicationBuilder AddDemoOpenTelemetry(this IHostApplicationBuilder, string serviceName, Action<TracerProviderBuilder>? configureTracing = null, Action<MeterProviderBuilder>? configureMetrics = null)`
- `IServiceCollection AddDemoResilienceTelemetry(this IServiceCollection)`
- `HostApplicationBuilder DemoHost.CreateBuilder(string[] args, string exampleName)` — registers OTel, resilience telemetry, `FlakyApiClientFactory`, raw client `DemoHost.RawClientName`.
- `Task<int> DemoHost.RunAsync(HostApplicationBuilder builder, string description, Func<IServiceProvider, CancellationToken, Task> scenario)`
- `DemoNarrator.Step(string title, string why) : DemoStep (IDisposable)`, `Info/Ok/Fail/Note(string)`, `Task<ApiResult?> DemoNarrator.CallAsync(Func<Task<ApiResult>> call)`
- `FlakyApiClient` methods: `OkAsync, FailThenOkAsync(int failures, string key), UnstableAsync(double failRate), SlowAsync(int ms), StatusAsync(int code, int? retryAfterSeconds), CreateOrderAsync, IsReachableAsync`
- `FlakyApiClientFactory.Create(string clientName) : FlakyApiClient`
- `ApiResult(int StatusCode, string? Region, string Body, TimeSpan Elapsed)`, `IsSuccess`
- `FlakyApiEndpoints.Primary = "http://localhost:5100"`, `FlakyApiEndpoints.SlowRegion = "http://localhost:5101"`
- `AppSettingsFile.SetValue(string colonPath, JsonNode value)`, `ReadAll()`, `WriteAll(string)`, `WaitForOptionsAsync<T>(IOptionsMonitor<T>, string? name, Func<T,bool>, TimeSpan)`

- [x] **Step 1: Root build files**

`Directory.Build.props`:
```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <InvariantGlobalization>true</InvariantGlobalization>
  </PropertyGroup>
</Project>
```

`Directory.Packages.props`:
```xml
<Project>
  <ItemGroup>
    <!-- Hosting / DI -->
    <PackageVersion Include="Microsoft.Extensions.Hosting" Version="10.0.12" />
    <!-- Resilience (Polly 8.8 comes transitively) -->
    <PackageVersion Include="Microsoft.Extensions.Resilience" Version="10.10.0" />
    <PackageVersion Include="Microsoft.Extensions.Http.Resilience" Version="10.10.0" />
    <PackageVersion Include="Microsoft.Extensions.Diagnostics.ExceptionSummarization" Version="10.10.0" />
    <!-- OpenTelemetry -->
    <PackageVersion Include="OpenTelemetry.Extensions.Hosting" Version="1.19.1" />
    <PackageVersion Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" Version="1.19.1" />
    <PackageVersion Include="OpenTelemetry.Instrumentation.Http" Version="1.19.0" />
    <PackageVersion Include="OpenTelemetry.Instrumentation.AspNetCore" Version="1.19.0" />
    <PackageVersion Include="OpenTelemetry.Instrumentation.Runtime" Version="1.19.0" />
  </ItemGroup>
</Project>
```

`.gitignore`:
```
bin/
obj/
.vs/
*.user
```

`TutorialNetResilience.slnx`:
```xml
<Solution>
  <Folder Name="/src/">
    <Project Path="src/FlakyApi/FlakyApi.csproj" />
    <Project Path="src/Resilience.Shared/Resilience.Shared.csproj" />
    <Project Path="src/Example01.StandardHandler/Example01.StandardHandler.csproj" />
    <Project Path="src/Example02.CustomPipeline/Example02.CustomPipeline.csproj" />
    <Project Path="src/Example03.Hedging/Example03.Hedging.csproj" />
    <Project Path="src/Example04.DynamicReload/Example04.DynamicReload.csproj" />
    <Project Path="src/Example05.NonHttpPipelines/Example05.NonHttpPipelines.csproj" />
    <Project Path="src/Example06.Chaos/Example06.Chaos.csproj" />
  </Folder>
</Solution>
```
(Projects that don't exist yet make `dotnet build <slnx>` fail; until Task 8 build individual projects with `dotnet build src/<Project>`.)

- [x] **Step 2: `src/Resilience.Shared/Resilience.Shared.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Hosting" />
    <PackageReference Include="Microsoft.Extensions.Resilience" />
    <PackageReference Include="Microsoft.Extensions.Http.Resilience" />
    <PackageReference Include="Microsoft.Extensions.Diagnostics.ExceptionSummarization" />
    <PackageReference Include="OpenTelemetry.Extensions.Hosting" />
    <PackageReference Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" />
    <PackageReference Include="OpenTelemetry.Instrumentation.Http" />
    <PackageReference Include="OpenTelemetry.Instrumentation.Runtime" />
  </ItemGroup>
</Project>
```

- [x] **Step 3: `DemoTelemetry.cs` + `FlakyApiEndpoints.cs` + `ApiResult.cs`**

```csharp
// DemoTelemetry.cs
using System.Diagnostics;

namespace Resilience.Shared;

/// <summary>The one ActivitySource used by every demo scenario (each step = one root span).</summary>
public static class DemoTelemetry
{
    public const string ActivitySourceName = "Resilience.Demo";
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
}
```

```csharp
// FlakyApiEndpoints.cs
namespace Resilience.Shared;

public static class FlakyApiEndpoints
{
    /// <summary>Region "eu-west": answers immediately.</summary>
    public const string Primary = "http://localhost:5100";

    /// <summary>Region "eu-east": same API, but every request gets +2500 ms of latency.</summary>
    public const string SlowRegion = "http://localhost:5101";
}
```

```csharp
// ApiResult.cs
namespace Resilience.Shared;

/// <summary>What a scenario needs to narrate an HTTP call: status, which region answered, how long it took.</summary>
public sealed record ApiResult(int StatusCode, string? Region, string Body, TimeSpan Elapsed)
{
    public bool IsSuccess => StatusCode is >= 200 and < 300;

    public override string ToString()
        => $"HTTP {StatusCode} from '{Region ?? "?"}' in {Elapsed.TotalMilliseconds:N0} ms";
}
```

- [x] **Step 4: `OpenTelemetryExtensions.cs`**

```csharp
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
```

- [x] **Step 5: `PollySpanEventsListener.cs`**

```csharp
using System.Diagnostics;
using Polly.Hedging;
using Polly.Retry;
using Polly.Telemetry;

namespace Resilience.Shared;

/// <summary>
/// Polly has metrics and logs but no tracing. This listener receives every Polly telemetry event
/// (OnRetry, OnCircuitOpened, OnHedging, OnTimeout, OnFallback, chaos injections, ...) and adds it as
/// an ActivityEvent on the current span, so a trace viewer shows *when* each resilience decision
/// happened next to the per-attempt HTTP spans.
/// </summary>
public sealed class PollySpanEventsListener : TelemetryListener
{
    public override void Write<TResult, TArgs>(in TelemetryEventArguments<TResult, TArgs> args)
    {
        var activity = Activity.Current;
        if (activity is null)
        {
            return;
        }

        var tags = new ActivityTagsCollection
        {
            { "polly.pipeline", args.Source.PipelineName },
            { "polly.strategy", args.Source.StrategyName },
            { "polly.event", args.Event.EventName },
            { "polly.severity", args.Event.Severity.ToString() },
            { "polly.operation_key", args.Context.OperationKey },
        };

        switch (args.Arguments)
        {
            case OnRetryArguments<TResult> retry:
                tags["polly.attempt"] = retry.AttemptNumber;
                tags["polly.retry_delay_ms"] = retry.RetryDelay.TotalMilliseconds;
                break;
            case OnHedgingArguments<TResult> hedging:
                tags["polly.attempt"] = hedging.AttemptNumber;
                break;
            case ExecutionAttemptArguments attempt:
                tags["polly.attempt"] = attempt.AttemptNumber;
                tags["polly.handled"] = attempt.Handled;
                tags["polly.duration_ms"] = attempt.Duration.TotalMilliseconds;
                break;
        }

        if (args.Outcome is { } outcome)
        {
            if (outcome.Exception is not null)
            {
                tags["polly.exception"] = outcome.Exception.GetType().Name;
            }
            else if (outcome.Result is HttpResponseMessage response)
            {
                tags["polly.http.status"] = (int)response.StatusCode;
            }
        }

        activity.AddEvent(new ActivityEvent($"polly.{args.Event.EventName}", tags: tags));
    }
}
```

- [x] **Step 6: `ResilienceTelemetryExtensions.cs`**

```csharp
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
    /// </summary>
    public static IServiceCollection AddDemoResilienceTelemetry(this IServiceCollection services)
    {
        services.AddExceptionSummarizer(builder => builder.AddHttpProvider());
        services.AddResilienceEnricher();
        services.Configure<TelemetryOptions>(options =>
            options.TelemetryListeners.Add(new PollySpanEventsListener()));
        return services;
    }
}
```

- [x] **Step 7: `DemoNarrator.cs`**

```csharp
using System.Diagnostics;
using System.Text;
using Polly.CircuitBreaker;
using Polly.RateLimiting;
using Polly.Timeout;

namespace Resilience.Shared;

/// <summary>Console narration: numbered steps (each one is also a root span), colored outcomes.</summary>
public static class DemoNarrator
{
    private static int _stepNumber;

    public static string ExampleName { get; set; } = "Example";

    public static void Title(string exampleName, string description)
    {
        Console.OutputEncoding = Encoding.UTF8;
        ExampleName = exampleName;
        Console.WriteLine();
        Write(ConsoleColor.White, $"=== {exampleName} ===");
        Write(ConsoleColor.Gray, description);
    }

    public static DemoStep Step(string title, string why)
    {
        var number = Interlocked.Increment(ref _stepNumber);
        Console.WriteLine();
        Write(ConsoleColor.Cyan, $"── Step {number}: {title}");
        Write(ConsoleColor.DarkGray, $"   Why: {why}");

        var activity = DemoTelemetry.ActivitySource.StartActivity($"{ExampleName} / step {number}: {title}");
        activity?.SetTag("demo.example", ExampleName);
        activity?.SetTag("demo.step", number);
        activity?.SetTag("demo.step.title", title);
        return new DemoStep(activity);
    }

    public static void Info(string message) => Write(ConsoleColor.Gray, $"   {message}");
    public static void Ok(string message) => Write(ConsoleColor.Green, $"   ✔ {message}");
    public static void Fail(string message) => Write(ConsoleColor.Yellow, $"   ✖ {message}");
    public static void Note(string message) => Write(ConsoleColor.Magenta, $"   ↳ {message}");

    /// <summary>
    /// Runs one HTTP call and prints the outcome. Resilience exceptions are *expected* outcomes in
    /// these demos, so they are printed, not rethrown. Anything else propagates.
    /// </summary>
    public static async Task<ApiResult?> CallAsync(Func<Task<ApiResult>> call)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await call();
            var line = $"{result} — {Truncate(result.Body)}";
            if (result.IsSuccess) Ok(line); else Fail(line);
            return result;
        }
        catch (BrokenCircuitException ex)
        {
            Fail($"{ex.GetType().Name} after {stopwatch.ElapsedMilliseconds} ms — {ex.Message}");
        }
        catch (TimeoutRejectedException ex)
        {
            Fail($"TimeoutRejectedException after {stopwatch.ElapsedMilliseconds} ms — {ex.Message}");
        }
        catch (RateLimiterRejectedException ex)
        {
            Fail($"RateLimiterRejectedException after {stopwatch.ElapsedMilliseconds} ms — retry after {ex.RetryAfter?.TotalMilliseconds:N0} ms");
        }
        catch (HttpRequestException ex)
        {
            Fail($"HttpRequestException after {stopwatch.ElapsedMilliseconds} ms — {ex.Message}");
        }
        catch (TaskCanceledException ex)
        {
            Fail($"TaskCanceledException after {stopwatch.ElapsedMilliseconds} ms — {ex.Message}");
        }

        return null;
    }

    private static string Truncate(string body)
    {
        var flat = body.Replace('\n', ' ').Replace('\r', ' ');
        return flat.Length <= 90 ? flat : flat[..90] + "…";
    }

    private static void Write(ConsoleColor color, string text)
    {
        var previous = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine(text);
        Console.ForegroundColor = previous;
    }
}

public sealed class DemoStep(Activity? activity) : IDisposable
{
    public Activity? Activity { get; } = activity;

    public void Dispose() => Activity?.Dispose();
}
```

- [x] **Step 8: `FlakyApiClient.cs` + `FlakyApiClientFactory.cs`**

```csharp
// FlakyApiClient.cs
using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;

namespace Resilience.Shared;

/// <summary>
/// Thin wrapper over an HttpClient that talks to FlakyApi. It never throws on non-2xx: the status
/// code is part of the result so the narrator can print "500 on attempt 1, 200 on attempt 3".
/// Resilience exceptions (BrokenCircuitException, TimeoutRejectedException, ...) do propagate.
/// </summary>
public sealed class FlakyApiClient(HttpClient http)
{
    public Task<ApiResult> OkAsync(CancellationToken ct = default)
        => SendAsync(HttpMethod.Get, "/ok", ct);

    public Task<ApiResult> FailThenOkAsync(int failures, string key, CancellationToken ct = default)
        => SendAsync(HttpMethod.Get, $"/fail-then-ok?failures={failures}&key={Uri.EscapeDataString(key)}", ct);

    public Task<ApiResult> UnstableAsync(double failRate, CancellationToken ct = default)
        => SendAsync(HttpMethod.Get, $"/unstable?failRate={failRate.ToString(CultureInfo.InvariantCulture)}", ct);

    public Task<ApiResult> SlowAsync(int ms, CancellationToken ct = default)
        => SendAsync(HttpMethod.Get, $"/slow?ms={ms}", ct);

    public Task<ApiResult> StatusAsync(int code, int? retryAfterSeconds = null, CancellationToken ct = default)
        => SendAsync(HttpMethod.Get, retryAfterSeconds is null ? $"/status/{code}" : $"/status/{code}?retryAfter={retryAfterSeconds}", ct);

    public Task<ApiResult> CreateOrderAsync(CancellationToken ct = default)
        => SendAsync(HttpMethod.Post, "/orders", ct);

    public async Task<bool> IsReachableAsync(CancellationToken ct = default)
    {
        try
        {
            return (await OkAsync(ct)).IsSuccess;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    private async Task<ApiResult> SendAsync(HttpMethod method, string path, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        using var request = new HttpRequestMessage(method, path);
        if (method == HttpMethod.Post)
        {
            request.Content = JsonContent.Create(new { item = "demo", quantity = 1 });
        }

        using var response = await http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        stopwatch.Stop();

        response.Headers.TryGetValues("X-Region", out var regions);
        return new ApiResult((int)response.StatusCode, regions?.FirstOrDefault(), body, stopwatch.Elapsed);
    }
}
```

```csharp
// FlakyApiClientFactory.cs
namespace Resilience.Shared;

/// <summary>Examples register several named HttpClients with different resilience setups; this picks one by name.</summary>
public sealed class FlakyApiClientFactory(IHttpClientFactory factory)
{
    public FlakyApiClient Create(string clientName) => new(factory.CreateClient(clientName));
}
```

- [x] **Step 9: `DemoHost.cs`**

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Resilience.Shared;

/// <summary>Common bootstrap for every example: host, logging, OpenTelemetry, resilience telemetry, FlakyApi clients.</summary>
public static class DemoHost
{
    /// <summary>Named HttpClient with NO resilience handler: used for the reachability check and for "before" comparisons.</summary>
    public const string RawClientName = "raw";

    public static HostApplicationBuilder CreateBuilder(string[] args, string exampleName)
    {
        var builder = Host.CreateApplicationBuilder(args);

        // Keep the console readable: single-line logs with a timestamp. Log levels come from appsettings.json.
        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(options =>
        {
            options.SingleLine = true;
            options.TimestampFormat = "HH:mm:ss.fff ";
        });

        builder.AddDemoOpenTelemetry(exampleName);
        builder.Services.AddDemoResilienceTelemetry();
        builder.Services.AddSingleton<FlakyApiClientFactory>();
        builder.Services.AddHttpClient(RawClientName, client => client.BaseAddress = new Uri(FlakyApiEndpoints.Primary));

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
```

- [x] **Step 10: `AppSettingsFile.cs`** (used by Example04 and Example06 to change configuration while running)

```csharp
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace Resilience.Shared;

/// <summary>
/// Edits the appsettings.json that the running process loaded (the copy in the output folder),
/// so "reloadOnChange" configuration kicks in without restarting the app.
/// </summary>
public static class AppSettingsFile
{
    public static string Path => System.IO.Path.Combine(AppContext.BaseDirectory, "appsettings.json");

    public static string ReadAll() => File.ReadAllText(Path);

    public static void WriteAll(string content) => File.WriteAllText(Path, content);

    /// <summary>Sets a value addressed with configuration syntax, e.g. "Resilience:Retry:MaxRetryAttempts".</summary>
    public static void SetValue(string colonPath, JsonNode value)
    {
        var root = JsonNode.Parse(ReadAll())!.AsObject();
        var segments = colonPath.Split(':');
        var current = root;
        for (var i = 0; i < segments.Length - 1; i++)
        {
            current = current[segments[i]]?.AsObject()
                ?? throw new InvalidOperationException($"Section '{segments[i]}' not found in {Path}");
        }

        current[segments[^1]] = value;
        WriteAll(root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Waits until the options monitor reports a value satisfying <paramref name="predicate"/>.</summary>
    public static async Task<TOptions> WaitForOptionsAsync<TOptions>(
        IOptionsMonitor<TOptions> monitor,
        string? name,
        Func<TOptions, bool> predicate,
        TimeSpan timeout)
    {
        var optionsName = name ?? Options.DefaultName;
        var tcs = new TaskCompletionSource<TOptions>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = monitor.OnChange((options, changedName) =>
        {
            if ((changedName ?? Options.DefaultName) == optionsName && predicate(options))
            {
                tcs.TrySetResult(options);
            }
        });

        var current = monitor.Get(optionsName);
        if (predicate(current))
        {
            return current;
        }

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(timeout));
        if (completed != tcs.Task)
        {
            throw new TimeoutException($"Options '{typeof(TOptions).Name}'/'{optionsName}' did not reload within {timeout}.");
        }

        return await tcs.Task;
    }
}
```

- [x] **Step 11: Build**

Run: `dotnet build src/Resilience.Shared`
Expected: `Build succeeded.` with 0 warnings. If a package id/version fails to restore, check nuget.org for the exact id and fix `Directory.Packages.props`.

---

### Task 2: `FlakyApi`

**Files:**
- Create: `src/FlakyApi/FlakyApi.csproj`
- Create: `src/FlakyApi/appsettings.json`
- Create: `src/FlakyApi/RegionOptions.cs`
- Create: `src/FlakyApi/Program.cs`

**Interfaces:**
- Consumes: `AddDemoOpenTelemetry` (Task 1).
- Produces: HTTP endpoints listed in the spec §4.2, on ports 5100 (eu-west, 0 ms) and 5101 (eu-east, +2500 ms). Responses carry `X-Region` and `X-Handled-By-Port` headers.

- [x] **Step 1: csproj + appsettings**

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <ItemGroup>
    <ProjectReference Include="../Resilience.Shared/Resilience.Shared.csproj" />
    <PackageReference Include="OpenTelemetry.Instrumentation.AspNetCore" />
  </ItemGroup>
</Project>
```

```json
{
  "Kestrel": {
    "Endpoints": {
      "Primary": { "Url": "http://localhost:5100" },
      "SlowRegion": { "Url": "http://localhost:5101" }
    }
  },
  "Regions": {
    "5100": { "Name": "eu-west", "ExtraLatencyMs": 0 },
    "5101": { "Name": "eu-east", "ExtraLatencyMs": 2500 }
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

- [x] **Step 2: `RegionOptions.cs`**

```csharp
namespace FlakyApi;

/// <summary>Behavior of one "region" (= one listening port).</summary>
public sealed class RegionOptions
{
    public string Name { get; set; } = "unknown";
    public int ExtraLatencyMs { get; set; }
}
```

- [x] **Step 3: `Program.cs`**

```csharp
using System.Collections.Concurrent;
using FlakyApi;
using Microsoft.Extensions.Options;
using Resilience.Shared;

var builder = WebApplication.CreateBuilder(args);

builder.AddDemoOpenTelemetry(
    "FlakyApi",
    configureTracing: tracing => tracing.AddAspNetCoreInstrumentation(),
    configureMetrics: metrics => metrics.AddAspNetCoreInstrumentation());

builder.Services.Configure<Dictionary<string, RegionOptions>>(builder.Configuration.GetSection("Regions"));
// Per-key call counters for /fail-then-ok.
builder.Services.AddSingleton<ConcurrentDictionary<string, int>>();

var app = builder.Build();

// One process, two ports. The port decides the "region" and its artificial latency, so the
// hedging example can race a slow region against a fast one without running two processes.
app.Use(async (context, next) =>
{
    var regions = context.RequestServices.GetRequiredService<IOptions<Dictionary<string, RegionOptions>>>().Value;
    var port = context.Connection.LocalPort.ToString();
    var region = regions.TryGetValue(port, out var configured) ? configured : new RegionOptions();
    context.Items["region"] = region;

    context.Response.OnStarting(() =>
    {
        context.Response.Headers["X-Region"] = region.Name;
        context.Response.Headers["X-Handled-By-Port"] = port;
        return Task.CompletedTask;
    });

    if (region.ExtraLatencyMs > 0)
    {
        try
        {
            await Task.Delay(region.ExtraLatencyMs, context.RequestAborted);
        }
        catch (OperationCanceledException)
        {
            // The client gave up (timeout / hedge winner already found): stop working for nothing.
            return;
        }
    }

    await next(context);
});

app.MapGet("/ok", (HttpContext context) =>
    Results.Ok(new { message = "all good", region = RegionName(context), at = DateTimeOffset.UtcNow }));

// Deterministic transient failure: the first N calls for a key fail with 500, then it succeeds.
app.MapGet("/fail-then-ok", IResult (HttpContext context, ConcurrentDictionary<string, int> counters, ILogger<Program> logger, int failures = 2, string key = "default") =>
{
    var call = counters.AddOrUpdate(key, 1, (_, previous) => previous + 1);
    if (call <= failures)
    {
        logger.LogWarning("fail-then-ok key={Key}: simulated failure {Call}/{Failures}", key, call, failures);
        return Results.Problem(statusCode: 500, title: $"Simulated failure {call}/{failures} for key '{key}'");
    }

    return Results.Ok(new { message = $"success on call {call}", region = RegionName(context) });
});

// Random failures: failRate=1.0 always fails, 0.0 never fails.
app.MapGet("/unstable", IResult (HttpContext context, double failRate = 0.5) =>
    Random.Shared.NextDouble() < failRate
        ? Results.Problem(statusCode: 500, title: "Random failure")
        : Results.Ok(new { message = "lucky this time", region = RegionName(context) }));

// Slow endpoint. Honors cancellation: when the client times out, the server stops waiting too.
app.MapGet("/slow", async Task<IResult> (HttpContext context, int ms = 3000) =>
{
    try
    {
        await Task.Delay(ms, context.RequestAborted);
    }
    catch (OperationCanceledException)
    {
        return Results.StatusCode(499);
    }

    return Results.Ok(new { message = $"slept {ms} ms", region = RegionName(context) });
});

// Any status code, optionally with a Retry-After header (seconds).
app.MapGet("/status/{code:int}", (HttpContext context, int code, int? retryAfter) =>
{
    if (retryAfter is not null)
    {
        context.Response.Headers.RetryAfter = retryAfter.Value.ToString();
    }

    return Results.StatusCode(code);
});

// Always fails: used to show that unsafe methods (POST) should not be retried blindly.
app.MapPost("/orders", () =>
    Results.Problem(statusCode: 500, title: "Orders service is down (always fails on purpose)"));

app.Run();

static string RegionName(HttpContext context)
    => context.Items["region"] is RegionOptions region ? region.Name : "unknown";
```

- [x] **Step 4: Build and run**

Run: `dotnet build src/FlakyApi` → `Build succeeded.`
Run in background: `dotnet run --project src/FlakyApi --no-build`
Verify (PowerShell):
```powershell
(iwr http://localhost:5100/ok).Headers['X-Region']            # eu-west
Measure-Command { iwr http://localhost:5101/ok } | % TotalMilliseconds   # ~2500
1..3 | % { try { (iwr "http://localhost:5100/fail-then-ok?failures=2&key=t1").StatusCode } catch { $_.Exception.Response.StatusCode.value__ } }  # 500 500 200
try { iwr "http://localhost:5100/status/429?retryAfter=2" } catch { $_.Exception.Response.Headers['Retry-After'] }  # 2
```
Expected: values as in the comments.

---

### Task 3: `Example01.StandardHandler`

**Files:**
- Create: `src/Example01.StandardHandler/Example01.StandardHandler.csproj`
- Create: `src/Example01.StandardHandler/appsettings.json`
- Create: `src/Example01.StandardHandler/ResilienceConfig.cs`
- Create: `src/Example01.StandardHandler/Scenario.cs`
- Create: `src/Example01.StandardHandler/Program.cs`

**Interfaces:**
- Consumes: `DemoHost.CreateBuilder/RunAsync`, `DemoNarrator`, `FlakyApiClientFactory`, `FlakyApiEndpoints` (Task 1); FlakyApi endpoints (Task 2).
- Produces: named clients `ResilienceConfig.Defaults`, `FromConfig`, `NoUnsafeRetries`, `Raw`.

- [x] **Step 1: csproj + appsettings**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../Resilience.Shared/Resilience.Shared.csproj" />
    <!-- Listed explicitly so the reader sees which package this example is about. -->
    <PackageReference Include="Microsoft.Extensions.Http.Resilience" />
  </ItemGroup>
  <ItemGroup>
    <None Update="appsettings.json" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Warning",
      "Polly": "Information"
    }
  },
  "Resilience": {
    "Standard": {
      "Retry": {
        "MaxRetryAttempts": 5,
        "Delay": "00:00:00.300",
        "BackoffType": "Exponential",
        "UseJitter": true
      },
      "AttemptTimeout": { "Timeout": "00:00:02" },
      "TotalRequestTimeout": { "Timeout": "00:00:20" }
    }
  }
}
```

- [x] **Step 2: `ResilienceConfig.cs`**

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Resilience.Shared;

namespace Example01.StandardHandler;

/// <summary>
/// The standard resilience handler = 5 strategies stacked with production-ready defaults:
///   rate limiter (1000 concurrent) → total timeout (30 s) → retry (3×, exponential + jitter, 2 s base)
///   → circuit breaker (10 % failures over 30 s, min 100 calls, 5 s break) → attempt timeout (10 s).
/// It handles HTTP 5xx, 408, 429, HttpRequestException and TimeoutRejectedException.
/// </summary>
public static class ResilienceConfig
{
    public const string Defaults = "standard-defaults";
    public const string FromConfig = "standard-from-config";
    public const string NoUnsafeRetries = "standard-no-unsafe-retries";
    public const string Raw = "raw-opt-out";

    public static void Configure(IServiceCollection services, IConfiguration configuration)
    {
        // Global policy: EVERY HttpClient created through IHttpClientFactory in this app gets the
        // standard handler. Clients that need something different remove it and add their own.
        services.ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler());

        // A) Inherits the global default: nothing to add.
        services.AddHttpClient(Defaults, client => client.BaseAddress = new Uri(FlakyApiEndpoints.Primary));

        // B) Same 5 strategies, but the numbers come from appsettings.json ("Resilience:Standard").
        //    Ops can tune retries/timeouts per environment without a rebuild; the standard handler
        //    also reloads automatically when the section changes.
        services.AddHttpClient(FromConfig, client => client.BaseAddress = new Uri(FlakyApiEndpoints.Primary))
            .RemoveAllResilienceHandlers()
            .AddStandardResilienceHandler(configuration.GetSection("Resilience:Standard"));

        // C) Never retry POST/PUT/PATCH/DELETE/CONNECT: a retried POST can create duplicate orders.
        services.AddHttpClient(NoUnsafeRetries, client => client.BaseAddress = new Uri(FlakyApiEndpoints.Primary))
            .RemoveAllResilienceHandlers()
            .AddStandardResilienceHandler(options => options.Retry.DisableForUnsafeHttpMethods());

        // D) Opt out completely (e.g. a health-check client that must fail fast).
        services.AddHttpClient(Raw, client => client.BaseAddress = new Uri(FlakyApiEndpoints.Primary))
            .RemoveAllResilienceHandlers();
    }
}
```

- [x] **Step 3: `Scenario.cs`**

```csharp
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
            DemoNarrator.Info("Configured: 5 retries, 300 ms exponential backoff, 2 s attempt timeout. Calling /fail-then-ok?failures=4.");
            await DemoNarrator.CallAsync(() => client.FailThenOkAsync(failures: 4, key: NewKey(), ct));
            DemoNarrator.Info("Now /slow?ms=3000: each attempt exceeds the 2 s attempt timeout, so all attempts time out.");
            await DemoNarrator.CallAsync(() => client.SlowAsync(3000, ct));
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
```

- [x] **Step 4: `Program.cs`**

```csharp
using Example01.StandardHandler;
using Resilience.Shared;

var builder = DemoHost.CreateBuilder(args, "Example01.StandardHandler");
ResilienceConfig.Configure(builder.Services, builder.Configuration);

return await DemoHost.RunAsync(
    builder,
    "AddStandardResilienceHandler(): the recommended default for every HttpClient.",
    Scenario.RunAsync);
```

- [x] **Step 5: Build and run** (FlakyApi running)

Run: `dotnet build src/Example01.StandardHandler` → `Build succeeded.`
Run: `dotnet run --project src/Example01.StandardHandler --no-build`
Expected console (timings vary):
- Step 1: `✔ HTTP 200 from 'eu-west' … success on call 3`, preceded by two Polly `OnRetry` log lines.
- Step 2: `✔ HTTP 200 … success on call 5`; then `/slow` ends with `✖ TimeoutRejectedException` after ~(5+1)×2 s ≈ 12–13 s.
- Step 3: `✖ HTTP 429` after ~3 s, Polly log lines ~1 s apart.
- Step 4: `✖ HTTP 500` with **no** `OnRetry` log line.
- Step 5: `✖ HTTP 500 … Simulated failure 1/2` with no `OnRetry` line.
- Ends with `✔ Scenario complete.` and exit code 0.

---

### Task 4: `Example02.CustomPipeline`

**Files:**
- Create: `src/Example02.CustomPipeline/Example02.CustomPipeline.csproj` (same as Task 3 Step 1)
- Create: `src/Example02.CustomPipeline/appsettings.json` (only the `Logging` section from Task 3)
- Create: `src/Example02.CustomPipeline/ResilienceConfig.cs`
- Create: `src/Example02.CustomPipeline/Scenario.cs`
- Create: `src/Example02.CustomPipeline/Program.cs`

**Interfaces:**
- Consumes: Task 1 shared types; FlakyApi `/slow`, `/unstable`, `/ok`.
- Produces: `ResilienceConfig.ClientName`, `ResilienceConfig.CircuitState` (`CircuitBreakerStateProvider`), `ResilienceConfig.CircuitControl` (`CircuitBreakerManualControl`).

- [x] **Step 1: csproj + appsettings** — copy Task 3 Step 1, project name `Example02.CustomPipeline`, `appsettings.json` = `{ "Logging": { "LogLevel": { "Default": "Warning", "Polly": "Information" } } }`.

- [x] **Step 2: `ResilienceConfig.cs`**

```csharp
using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.CircuitBreaker;
using Polly.Fallback;
using Polly.Timeout;
using Resilience.Shared;

namespace Example02.CustomPipeline;

/// <summary>
/// AddResilienceHandler lets you compose your own pipeline. Order matters: strategies are added
/// outermost → innermost, and a request flows through them in that order.
///   fallback → retry → circuit breaker → attempt timeout → (HttpClient)
/// So: a timeout is retried; repeated failures open the breaker; when everything fails the
/// fallback returns a degraded answer instead of an exception.
/// </summary>
public static class ResilienceConfig
{
    public const string ClientName = "custom-pipeline";

    /// <summary>Read the breaker state from anywhere (dashboards, health checks, this demo).</summary>
    public static readonly CircuitBreakerStateProvider CircuitState = new();

    /// <summary>Open/close the breaker by hand (maintenance windows, kill switch).</summary>
    public static readonly CircuitBreakerManualControl CircuitControl = new();

    public static void Configure(IServiceCollection services)
    {
        services.AddHttpClient(ClientName, client => client.BaseAddress = new Uri(FlakyApiEndpoints.Primary))
            .AddResilienceHandler("custom", builder =>
            {
                // 1. Fallback (outermost): only for timeouts, so the other failure modes stay visible in this demo.
                builder.AddFallback(new FallbackStrategyOptions<HttpResponseMessage>
                {
                    Name = "fallback",
                    ShouldHandle = new PredicateBuilder<HttpResponseMessage>().Handle<TimeoutRejectedException>(),
                    FallbackAction = _ =>
                    {
                        var cached = new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent("{\"message\":\"stale data served from cache\"}", Encoding.UTF8, "application/json"),
                        };
                        cached.Headers.Add("X-Region", "fallback-cache");
                        return Outcome.FromResultAsValueTask(cached);
                    },
                    OnFallback = args =>
                    {
                        DemoNarrator.Note($"fallback: serving cached data because of {Describe(args.Outcome)}");
                        return default;
                    },
                });

                // 2. Retry: 2 extra attempts, constant 200 ms. HttpRetryStrategyOptions already handles
                //    5xx/408/429, HttpRequestException and TimeoutRejectedException.
                builder.AddRetry(new HttpRetryStrategyOptions
                {
                    Name = "retry",
                    MaxRetryAttempts = 2,
                    Delay = TimeSpan.FromMilliseconds(200),
                    BackoffType = DelayBackoffType.Constant,
                    OnRetry = args =>
                    {
                        DemoNarrator.Note($"retry #{args.AttemptNumber + 1} in {args.RetryDelay.TotalMilliseconds:N0} ms because of {Describe(args.Outcome)}");
                        return default;
                    },
                });

                // 3. Circuit breaker: tuned small so the demo trips it with a handful of calls.
                //    Production defaults are 10 % over 30 s with at least 100 calls.
                builder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
                {
                    Name = "circuit-breaker",
                    SamplingDuration = TimeSpan.FromSeconds(10),
                    MinimumThroughput = 5,
                    FailureRatio = 0.5,
                    BreakDuration = TimeSpan.FromSeconds(5),
                    StateProvider = CircuitState,
                    ManualControl = CircuitControl,
                    OnOpened = args =>
                    {
                        DemoNarrator.Note($"circuit OPENED for {args.BreakDuration.TotalSeconds:N0} s (manual: {args.IsManual}) — calls now fail fast");
                        return default;
                    },
                    OnHalfOpened = _ =>
                    {
                        DemoNarrator.Note("circuit HALF-OPEN — one trial call is allowed through");
                        return default;
                    },
                    OnClosed = args =>
                    {
                        DemoNarrator.Note($"circuit CLOSED (manual: {args.IsManual}) — back to normal");
                        return default;
                    },
                });

                // 4. Attempt timeout (innermost): bounds a single try; the retry above decides what to do next.
                builder.AddTimeout(new HttpTimeoutStrategyOptions
                {
                    Name = "attempt-timeout",
                    Timeout = TimeSpan.FromSeconds(1),
                    OnTimeout = args =>
                    {
                        DemoNarrator.Note($"attempt timed out after {args.Timeout.TotalSeconds:N1} s");
                        return default;
                    },
                });
            });
    }

    private static string Describe(Outcome<HttpResponseMessage> outcome)
        => outcome.Exception is { } ex ? ex.GetType().Name : $"HTTP {(int)outcome.Result!.StatusCode}";
}
```

- [x] **Step 3: `Scenario.cs`**

```csharp
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
            DemoNarrator.Info("Calling /unstable?failRate=1 six times (min throughput 5, failure ratio 50 %).");
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
```

- [x] **Step 4: `Program.cs`**

```csharp
using Example02.CustomPipeline;
using Resilience.Shared;

var builder = DemoHost.CreateBuilder(args, "Example02.CustomPipeline");
ResilienceConfig.Configure(builder.Services);

return await DemoHost.RunAsync(
    builder,
    "AddResilienceHandler(): compose fallback, retry, circuit breaker and timeout by hand.",
    Scenario.RunAsync);
```

- [x] **Step 5: Build and run** (FlakyApi running)

Run: `dotnet build src/Example02.CustomPipeline` then `dotnet run --project src/Example02.CustomPipeline --no-build`
Expected:
- Step 1: three `attempt timed out` notes, two `retry #` notes, `fallback:` note, then `✔ HTTP 200 from 'fallback-cache'` after ~3.4 s.
- Step 2: call 1 → `✖ HTTP 500` with 2 retries; during call 2 `circuit OPENED for 5 s`; calls 3–6 → `✖ BrokenCircuitException after 0 ms`.
- Step 3: `circuit HALF-OPEN`, `✔ HTTP 200`, `circuit CLOSED`, state `Closed`.
- Step 4: `Isolated → Isolated`, `✖ IsolatedCircuitException`, `Closed → Closed`, `✔ HTTP 200`.

---

### Task 5: `Example03.Hedging`

**Files:**
- Create: `src/Example03.Hedging/Example03.Hedging.csproj` (same as Task 3 Step 1)
- Create: `src/Example03.Hedging/appsettings.json` (Logging section only, as Task 4)
- Create: `src/Example03.Hedging/ResilienceConfig.cs`
- Create: `src/Example03.Hedging/Scenario.cs`
- Create: `src/Example03.Hedging/Program.cs`

**Interfaces:**
- Consumes: Task 1 shared types; FlakyApi on both ports, `/slow`, `/ok`.
- Produces: named clients `ResilienceConfig.NoHedging`, `OrderedHedging`, `WeightedHedging`.

- [x] **Step 1: csproj + appsettings** — as Task 4 Step 1 with project name `Example03.Hedging`.

- [x] **Step 2: `ResilienceConfig.cs`**

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Resilience.Shared;

namespace Example03.Hedging;

/// <summary>
/// Hedging = "don't wait for a slow attempt, start another one in parallel and take the first answer".
/// The standard hedging handler stacks: total timeout → hedging → [per endpoint: rate limiter →
/// circuit breaker → attempt timeout]. Routing decides where each attempt goes; only scheme, host
/// and port of the request URI are replaced, path and query are kept.
/// </summary>
public static class ResilienceConfig
{
    public const string NoHedging = "no-hedging";
    public const string OrderedHedging = "ordered-hedging";
    public const string WeightedHedging = "weighted-hedging";

    private static readonly Uri SlowRegion = new(FlakyApiEndpoints.SlowRegion);
    private static readonly Uri FastRegion = new(FlakyApiEndpoints.Primary);

    public static void Configure(IServiceCollection services)
    {
        // Baseline: the slow region, no resilience at all.
        services.AddHttpClient(NoHedging, client => client.BaseAddress = SlowRegion);

        // Ordered groups: attempt 1 goes to group 1 (slow region). If it has not answered after
        // Hedging.Delay, attempt 2 goes to group 2 (fast region). First response wins, the loser
        // is cancelled. Max attempts = number of groups.
        services.AddHttpClient(OrderedHedging, client => client.BaseAddress = SlowRegion)
            .AddStandardHedgingHandler(routing => routing.ConfigureOrderedGroups(options =>
            {
                options.Groups.Add(new UriEndpointGroup { Endpoints = { new() { Uri = SlowRegion, Weight = 1 } } });
                options.Groups.Add(new UriEndpointGroup { Endpoints = { new() { Uri = FastRegion, Weight = 1 } } });
            }))
            .Configure(options =>
            {
                options.Hedging.Delay = TimeSpan.FromSeconds(1);
                options.Hedging.MaxHedgedAttempts = 2;
                options.Endpoint.Timeout.Timeout = TimeSpan.FromSeconds(10);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(20);
            })
            // One circuit breaker / rate limiter / timeout per authority (scheme+host+port):
            // an unhealthy region trips its own breaker without affecting the other.
            .SelectPipelineByAuthority();

        // Weighted groups: the FIRST attempt picks a group by weight (A/B, canary, traffic split);
        // later hedged attempts go to the remaining groups.
        services.AddHttpClient(WeightedHedging, client => client.BaseAddress = FastRegion)
            .AddStandardHedgingHandler(routing => routing.ConfigureWeightedGroups(options =>
            {
                options.SelectionMode = WeightedGroupSelectionMode.InitialAttempt;
                options.Groups.Add(new WeightedUriEndpointGroup { Weight = 50, Endpoints = { new() { Uri = SlowRegion } } });
                options.Groups.Add(new WeightedUriEndpointGroup { Weight = 50, Endpoints = { new() { Uri = FastRegion } } });
            }))
            .Configure(options =>
            {
                options.Hedging.Delay = TimeSpan.FromSeconds(1);
                options.Hedging.MaxHedgedAttempts = 2;
            })
            .SelectPipelineByAuthority();
    }
}
```

- [x] **Step 3: `Scenario.cs`**

```csharp
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
```

- [x] **Step 4: `Program.cs`**

```csharp
using Example03.Hedging;
using Resilience.Shared;

var builder = DemoHost.CreateBuilder(args, "Example03.Hedging");
ResilienceConfig.Configure(builder.Services);

return await DemoHost.RunAsync(
    builder,
    "AddStandardHedgingHandler(): race a slow endpoint against a fast one.",
    Scenario.RunAsync);
```

- [x] **Step 5: Build and run** (FlakyApi running)

Expected:
- Step 1: `✔ HTTP 200 from 'eu-east' in ~4000 ms`.
- Step 2: `✔ HTTP 200 from 'eu-west' in ~2500 ms`.
- Step 3: each call `✔ HTTP 200 from 'eu-west'`, some in ~0–50 ms, some in ~1000 ms (hedged after starting on eu-east).

---

### Task 6: `Example04.DynamicReload`

**Files:**
- Create: `src/Example04.DynamicReload/Example04.DynamicReload.csproj` (same as Task 3 Step 1)
- Create: `src/Example04.DynamicReload/appsettings.json`
- Create: `src/Example04.DynamicReload/ResilienceConfig.cs`
- Create: `src/Example04.DynamicReload/Scenario.cs`
- Create: `src/Example04.DynamicReload/Program.cs`

**Interfaces:**
- Consumes: `AppSettingsFile.SetValue/ReadAll/WriteAll/WaitForOptionsAsync` (Task 1); FlakyApi `/fail-then-ok`.
- Produces: `ResilienceConfig.ClientName`, `ResilienceConfig.RetryOptionsName = "Retry"`.

- [x] **Step 1: csproj + appsettings**

csproj as Task 3 Step 1 with project name `Example04.DynamicReload`.

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Warning",
      "Polly": "Information"
    }
  },
  "Resilience": {
    "Retry": {
      "MaxRetryAttempts": 1,
      "Delay": "00:00:00.200",
      "BackoffType": "Constant",
      "UseJitter": false
    }
  }
}
```

- [x] **Step 2: `ResilienceConfig.cs`**

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
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
```

- [x] **Step 3: `Scenario.cs`**

```csharp
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
```

- [x] **Step 4: `Program.cs`**

```csharp
using Example04.DynamicReload;
using Resilience.Shared;

var builder = DemoHost.CreateBuilder(args, "Example04.DynamicReload");
ResilienceConfig.Configure(builder.Services, builder.Configuration);

return await DemoHost.RunAsync(
    builder,
    "EnableReloads(): change retry settings at runtime by editing appsettings.json.",
    Scenario.RunAsync);
```

- [x] **Step 5: Build and run** (FlakyApi running)

Expected:
- Step 1: `pipeline built with MaxRetryAttempts = 1`, one `retry #1`, `✖ HTTP 500 … Simulated failure 2/3`.
- Step 2: `✔ Options reloaded: MaxRetryAttempts = 4`, `pipeline built with MaxRetryAttempts = 4`, three `retry #` notes, `✔ HTTP 200 … success on call 4`.
- `appsettings.json restored`. Running it twice in a row gives the same output.

---

### Task 7: `Example05.NonHttpPipelines`

**Files:**
- Create: `src/Example05.NonHttpPipelines/Example05.NonHttpPipelines.csproj`
- Create: `src/Example05.NonHttpPipelines/appsettings.json` (Logging only, as Task 4)
- Create: `src/Example05.NonHttpPipelines/FakeOrdersRepository.cs`
- Create: `src/Example05.NonHttpPipelines/ResilienceConfig.cs`
- Create: `src/Example05.NonHttpPipelines/Scenario.cs`
- Create: `src/Example05.NonHttpPipelines/Program.cs`

**Interfaces:**
- Consumes: Task 1 shared types (no FlakyApi calls except the reachability ping).
- Produces: pipeline keys `ResilienceConfig.SavePipeline`, `ReadPipeline`, `BulkheadPipeline`, `ThrottlePipeline`; `Order` record; `FakeOrdersRepository`.

- [x] **Step 1: csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../Resilience.Shared/Resilience.Shared.csproj" />
    <!-- Non-HTTP pipelines only need Microsoft.Extensions.Resilience (Polly.Core/Extensions/RateLimiting come with it). -->
    <PackageReference Include="Microsoft.Extensions.Resilience" />
  </ItemGroup>
  <ItemGroup>
    <None Update="appsettings.json" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

- [x] **Step 2: `FakeOrdersRepository.cs`**

```csharp
namespace Example05.NonHttpPipelines;

public sealed record Order(int Id, string Customer, decimal Total);

/// <summary>Stands in for a database: transient errors, replica lag (null reads) and slow queries on demand.</summary>
public sealed class FakeOrdersRepository
{
    private int _saveFailuresRemaining;
    private int _readMissesRemaining;
    private int _activeQueries;

    public void ArmSaveFailures(int count) => _saveFailuresRemaining = count;

    public void ArmReadMisses(int count) => _readMissesRemaining = count;

    public async Task SaveAsync(Order order, CancellationToken ct)
    {
        await Task.Delay(50, ct);
        if (Interlocked.Decrement(ref _saveFailuresRemaining) >= 0)
        {
            throw new TimeoutException("Simulated deadlock / transient database error");
        }
    }

    public async Task<Order?> GetAsync(int id, CancellationToken ct)
    {
        await Task.Delay(50, ct);
        return Interlocked.Decrement(ref _readMissesRemaining) >= 0
            ? null // replica lag: the row is not visible yet
            : new Order(id, "ACME", 99.90m);
    }

    /// <summary>A slow query; reports how many were running at the same time.</summary>
    public async Task<int> CountAsync(CancellationToken ct)
    {
        var concurrent = Interlocked.Increment(ref _activeQueries);
        try
        {
            await Task.Delay(500, ct);
            return concurrent;
        }
        finally
        {
            Interlocked.Decrement(ref _activeQueries);
        }
    }
}
```

- [x] **Step 3: `ResilienceConfig.cs`**

```csharp
using System.Threading.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.RateLimiting;
using Polly.Retry;
using Polly.Timeout;
using Resilience.Shared;

namespace Example05.NonHttpPipelines;

/// <summary>
/// Resilience is not only for HttpClient. Any operation (database, queue, cache, gRPC) can run
/// inside a ResiliencePipeline. Pipelines are registered by key in DI and resolved through
/// ResiliencePipelineProvider&lt;string&gt;; telemetry is enabled automatically.
/// </summary>
public static class ResilienceConfig
{
    public const string SavePipeline = "orders-db-save";
    public const string ReadPipeline = "orders-db-read";
    public const string BulkheadPipeline = "orders-db-bulkhead";
    public const string ThrottlePipeline = "orders-db-throttle";

    public static void Configure(IServiceCollection services)
    {
        services.AddSingleton<FakeOrdersRepository>();

        // Non-generic pipeline (the result does not matter): retry on transient exceptions + per-attempt timeout.
        services.AddResiliencePipeline(SavePipeline, builder =>
        {
            builder
                .AddRetry(new RetryStrategyOptions
                {
                    Name = "db-retry",
                    ShouldHandle = new PredicateBuilder().Handle<TimeoutException>().Handle<TimeoutRejectedException>(),
                    MaxRetryAttempts = 3,
                    Delay = TimeSpan.FromMilliseconds(100),
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    OnRetry = args =>
                    {
                        DemoNarrator.Note($"retry #{args.AttemptNumber + 1} after {args.Outcome.Exception?.GetType().Name}");
                        return default;
                    },
                })
                .AddTimeout(TimeSpan.FromSeconds(2));
        });

        // Generic pipeline: the RESULT decides. A null read (replica lag) is retried like a failure.
        services.AddResiliencePipeline<string, Order?>(ReadPipeline, builder =>
        {
            builder.AddRetry(new RetryStrategyOptions<Order?>
            {
                Name = "db-read-retry",
                ShouldHandle = new PredicateBuilder<Order?>().HandleResult(order => order is null).Handle<TimeoutException>(),
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromMilliseconds(100),
                OnRetry = args =>
                {
                    DemoNarrator.Note($"retry #{args.AttemptNumber + 1} because the result was {(args.Outcome.Result is null ? "null" : "an exception")}");
                    return default;
                },
            });
        });

        // Bulkhead: at most 2 executions at a time, no queue. Protects the database (and the rest of
        // this process) from a burst that would exhaust connections or threads.
        services.AddResiliencePipeline(BulkheadPipeline, builder =>
        {
            builder.AddConcurrencyLimiter(permitLimit: 2, queueLimit: 0);
        });

        // Throttle: 3 executions per second. Any System.Threading.RateLimiting limiter can be plugged in.
        services.AddResiliencePipeline(ThrottlePipeline, builder =>
        {
            builder.AddRateLimiter(new SlidingWindowRateLimiter(new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 3,
                Window = TimeSpan.FromSeconds(1),
                SegmentsPerWindow = 1,
                QueueLimit = 0,
            }));
        });
    }
}
```

- [x] **Step 4: `Scenario.cs`**

```csharp
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
                    DemoNarrator.Fail($"call {i}: throttled, retry after {ex.RetryAfter?.TotalMilliseconds:N0} ms");
                }
            }
        }
    }
}
```

- [x] **Step 5: `Program.cs`**

```csharp
using Example05.NonHttpPipelines;
using Resilience.Shared;

var builder = DemoHost.CreateBuilder(args, "Example05.NonHttpPipelines");
ResilienceConfig.Configure(builder.Services);

return await DemoHost.RunAsync(
    builder,
    "AddResiliencePipeline(): retry, bulkhead and throttling for non-HTTP dependencies.",
    Scenario.RunAsync);
```

- [x] **Step 6: Build and run** (FlakyApi running, for the ping only)

Expected:
- Step 1: two `retry #` notes then `✔ Order saved after two transient failures.`
- Step 2: two `retry # … result was null` notes then `✔ Read result: Order { Id = 7, … }`.
- Step 3: two `✔ caller N: ran with 2 concurrent queries`, four `✖ caller N: rejected`.
- Step 4: calls 1–3 `✔`, calls 4–5 `✖ throttled`.

---

### Task 8: `Example06.Chaos`

**Files:**
- Create: `src/Example06.Chaos/Example06.Chaos.csproj` (same as Task 3 Step 1)
- Create: `src/Example06.Chaos/appsettings.json`
- Create: `src/Example06.Chaos/ChaosOptions.cs`
- Create: `src/Example06.Chaos/ResilienceConfig.cs`
- Create: `src/Example06.Chaos/Scenario.cs`
- Create: `src/Example06.Chaos/Program.cs`

**Interfaces:**
- Consumes: Task 1 shared types incl. `AppSettingsFile`; FlakyApi `/ok`.
- Produces: `ResilienceConfig.ClientName`, `ChaosOptions`.

- [x] **Step 1: csproj + appsettings**

csproj as Task 3 Step 1 with project name `Example06.Chaos`.

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Warning",
      "Polly": "Information"
    }
  },
  "Chaos": {
    "Enabled": false,
    "LatencyInjectionRate": 0.3,
    "LatencyMs": 2000,
    "FaultInjectionRate": 0.2,
    "OutcomeInjectionRate": 0.2
  }
}
```

- [x] **Step 2: `ChaosOptions.cs`**

```csharp
namespace Example06.Chaos;

/// <summary>Chaos knobs, bound from "Chaos" in appsettings.json. Keep Enabled=false in Production.</summary>
public sealed class ChaosOptions
{
    public bool Enabled { get; set; }
    public double LatencyInjectionRate { get; set; } = 0.3;
    public int LatencyMs { get; set; } = 2000;
    public double FaultInjectionRate { get; set; } = 0.2;
    public double OutcomeInjectionRate { get; set; } = 0.2;
}
```

- [x] **Step 3: `ResilienceConfig.cs`**

```csharp
using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Simmy;
using Polly.Simmy.Fault;
using Polly.Simmy.Latency;
using Polly.Simmy.Outcomes;
using Resilience.Shared;

namespace Example06.Chaos;

/// <summary>
/// Chaos engineering, built into Polly (formerly "Simmy"): inject latency, exceptions and fake
/// results at a configurable rate to prove the resilience strategies actually work.
/// Chaos strategies go LAST (innermost) so that retry / circuit breaker / timeout above them see
/// the injected faults exactly as they would see real ones.
/// </summary>
public static class ResilienceConfig
{
    public const string ClientName = "chaos-demo";

    public static void Configure(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ChaosOptions>(configuration.GetSection("Chaos"));

        services.AddHttpClient(ClientName, client => client.BaseAddress = new Uri(FlakyApiEndpoints.Primary))
            .AddResilienceHandler("chaos-pipeline", (builder, context) =>
            {
                // Read the knobs live: the generators below are evaluated on every execution, so
                // toggling "Chaos:Enabled" in appsettings.json takes effect without rebuilding the pipeline.
                var chaos = context.ServiceProvider.GetRequiredService<IOptionsMonitor<ChaosOptions>>();

                // --- the strategies under test -------------------------------------------------
                builder.AddRetry(new HttpRetryStrategyOptions
                {
                    Name = "retry",
                    MaxRetryAttempts = 3,
                    Delay = TimeSpan.FromMilliseconds(100),
                    BackoffType = DelayBackoffType.Constant,
                    OnRetry = args =>
                    {
                        DemoNarrator.Note($"retry #{args.AttemptNumber + 1} — {Describe(args.Outcome)}");
                        return default;
                    },
                });

                builder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
                {
                    Name = "circuit-breaker",
                    // Lenient on purpose: this demo wants to show retries absorbing chaos, not the breaker tripping.
                    MinimumThroughput = 50,
                    FailureRatio = 0.9,
                    SamplingDuration = TimeSpan.FromSeconds(30),
                    BreakDuration = TimeSpan.FromSeconds(5),
                });

                builder.AddTimeout(new HttpTimeoutStrategyOptions
                {
                    Name = "attempt-timeout",
                    Timeout = TimeSpan.FromSeconds(1),
                    OnTimeout = _ =>
                    {
                        DemoNarrator.Note("attempt timed out (1 s)");
                        return default;
                    },
                });

                // --- chaos, innermost ------------------------------------------------------------
                builder.AddChaosLatency(new ChaosLatencyStrategyOptions
                {
                    Name = "chaos-latency",
                    EnabledGenerator = _ => ValueTask.FromResult(chaos.CurrentValue.Enabled),
                    InjectionRateGenerator = _ => ValueTask.FromResult(chaos.CurrentValue.LatencyInjectionRate),
                    LatencyGenerator = _ => ValueTask.FromResult(TimeSpan.FromMilliseconds(chaos.CurrentValue.LatencyMs)),
                    OnLatencyInjected = args =>
                    {
                        DemoNarrator.Note($"chaos: +{args.Latency.TotalMilliseconds:N0} ms latency injected");
                        return default;
                    },
                });

                builder.AddChaosFault(new ChaosFaultStrategyOptions
                {
                    Name = "chaos-fault",
                    EnabledGenerator = _ => ValueTask.FromResult(chaos.CurrentValue.Enabled),
                    InjectionRateGenerator = _ => ValueTask.FromResult(chaos.CurrentValue.FaultInjectionRate),
                    FaultGenerator = new FaultGenerator().AddException(() => new HttpRequestException("chaos: connection reset by peer")),
                    OnFaultInjected = args =>
                    {
                        DemoNarrator.Note($"chaos: fault injected — {args.Fault.GetType().Name}");
                        return default;
                    },
                });

                builder.AddChaosOutcome(new ChaosOutcomeStrategyOptions<HttpResponseMessage>
                {
                    Name = "chaos-outcome",
                    EnabledGenerator = _ => ValueTask.FromResult(chaos.CurrentValue.Enabled),
                    InjectionRateGenerator = _ => ValueTask.FromResult(chaos.CurrentValue.OutcomeInjectionRate),
                    OutcomeGenerator = new OutcomeGenerator<HttpResponseMessage>()
                        .AddResult(() => new HttpResponseMessage(HttpStatusCode.InternalServerError) { ReasonPhrase = "chaos" }),
                    OnOutcomeInjected = args =>
                    {
                        DemoNarrator.Note($"chaos: outcome injected — {Describe(args.Outcome)}");
                        return default;
                    },
                });
            });
    }

    private static string Describe(Outcome<HttpResponseMessage> outcome)
        => outcome.Exception is { } ex ? ex.GetType().Name : $"HTTP {(int)outcome.Result!.StatusCode}";
}
```

- [x] **Step 4: `Scenario.cs`**

```csharp
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Resilience.Shared;

namespace Example06.Chaos;

public static class Scenario
{
    public static async Task RunAsync(IServiceProvider services, CancellationToken ct)
    {
        var client = services.GetRequiredService<FlakyApiClientFactory>().Create(ResilienceConfig.ClientName);
        var monitor = services.GetRequiredService<IOptionsMonitor<ChaosOptions>>();
        var originalJson = AppSettingsFile.ReadAll();

        try
        {
            using (DemoNarrator.Step("Chaos off",
                       "Baseline: the pipeline is there but nothing is injected. 10 calls to /ok, all fast."))
            {
                DemoNarrator.Info($"Chaos:Enabled = {monitor.CurrentValue.Enabled}");
                await RunBatchAsync(client, ct);
            }

            using (DemoNarrator.Step("Chaos on: 30 % latency, 20 % faults, 20 % fake 500s",
                       "Turn chaos on from configuration and watch retry + timeout absorb the injected failures. The caller should still get 10 × 200."))
            {
                AppSettingsFile.SetValue("Chaos:Enabled", JsonValue.Create(true));
                var reloaded = await AppSettingsFile.WaitForOptionsAsync(monitor, null, o => o.Enabled, TimeSpan.FromSeconds(15));
                DemoNarrator.Ok($"Chaos:Enabled = {reloaded.Enabled} (latency {reloaded.LatencyInjectionRate:P0}, faults {reloaded.FaultInjectionRate:P0}, outcomes {reloaded.OutcomeInjectionRate:P0})");
                await RunBatchAsync(client, ct);
                DemoNarrator.Note("Metrics: resilience.polly.strategy.events with event.name OnLatencyInjected / OnFaultInjected / OnOutcomeInjected, plus OnRetry and OnTimeout.");
            }

            using (DemoNarrator.Step("Keep chaos out of Production",
                       "Chaos is opt-in per environment: appsettings.Production.json (or an env var) sets Chaos:Enabled=false and the strategies become no-ops."))
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
```

- [x] **Step 5: `Program.cs`**

```csharp
using Example06.Chaos;
using Resilience.Shared;

var builder = DemoHost.CreateBuilder(args, "Example06.Chaos");
ResilienceConfig.Configure(builder.Services, builder.Configuration);

return await DemoHost.RunAsync(
    builder,
    "Chaos strategies: inject latency, faults and fake results to prove the pipeline holds.",
    Scenario.RunAsync);
```

- [x] **Step 6: Build and run** (FlakyApi running)

Expected:
- Step 1: 10 × `✔ HTTP 200 from 'eu-west'` in a few ms, `10/10 calls succeeded`.
- Step 2: several `chaos: …` notes, `attempt timed out`, `retry #` notes; almost always `10/10 calls succeeded` (a call fails only if 4 consecutive attempts are all hit by chaos, probability ≈ 0.7⁴·… < 1 %).
- Step 3: 3 × `✔ HTTP 200` with no chaos notes.

---

### Task 9: Whole-solution build, OTLP smoke test, README

**Files:**
- Create: `README.md` (Italian)
- Throwaway (scratchpad, not in the repo): `<scratchpad>/OtlpSink/Program.cs`

- [x] **Step 1: Full build**

Run: `dotnet build TutorialNetResilience.slnx`
Expected: `Build succeeded.` 0 Warning(s) 0 Error(s).

- [x] **Step 2: Throwaway OTLP/HTTP sink** in the scratchpad (`dotnet new web -n OtlpSink`), `Program.cs`:

```csharp
var app = WebApplication.CreateBuilder(args).Build();
app.MapPost("/v1/{signal}", async (string signal, HttpRequest request) =>
{
    using var ms = new MemoryStream();
    await request.Body.CopyToAsync(ms);
    Console.WriteLine($"{DateTime.Now:HH:mm:ss} {signal,-8} {ms.Length,7} bytes  {request.ContentType}");
    return Results.Ok();
});
app.Run("http://localhost:4318");
```

Run it, then in the shells for FlakyApi and one example set:
```powershell
$env:OTEL_EXPORTER_OTLP_ENDPOINT = "http://localhost:4318"
$env:OTEL_EXPORTER_OTLP_PROTOCOL = "http/protobuf"
```
Run FlakyApi + Example02. Expected sink output: lines for `traces`, `metrics` and `logs` from both services (metrics every 5 s). Stop the sink; do not commit it.

- [x] **Step 3: `README.md`** (Italian). Sections, in this order, each with the content described:

1. **Titolo + intro** — cos'è la resilienza, perché `Microsoft.Extensions.Resilience` (Polly v8 + DI + `IHttpClientFactory` + telemetria pronta), versioni usate (tabella pacchetti di Task 1).
2. **Prerequisiti** — .NET SDK 10, un ricevitore OTLP a scelta; variabili `OTEL_EXPORTER_OTLP_ENDPOINT` / `OTEL_EXPORTER_OTLP_PROTOCOL` (default gRPC `http://localhost:4317`); nota che senza ricevitore gli esempi funzionano comunque (l'SDK logga un warning).
3. **Struttura del repository** — albero come nella spec §3, una riga per progetto.
4. **Come eseguire** — blocco PowerShell: avviare `FlakyApi` in un terminale, poi `dotnet run --project src/ExampleNN...`; tabella degli endpoint di FlakyApi con le due regioni/porte.
5. **Gli esempi** — una sottosezione per esempio con: *Cosa fa*, *Perché*, *Cosa osservare* (console, tracce, metriche, log), e uno snippet della configurazione chiave preso da `ResilienceConfig.cs`. Per Example01 includere la tabella delle 5 strategie con i default (rate limiter 1000 / total timeout 30 s / retry 3 exp+jitter 2 s / circuit breaker 10 %-100 calls-30 s-5 s / attempt timeout 10 s) e la lista degli errori gestiti (5xx, 408, 429, `HttpRequestException`, `TimeoutRejectedException`).
6. **Telemetria** — come è cablata (`AddDemoOpenTelemetry`, `AddDemoResilienceTelemetry`, `PollySpanEventsListener`), tabella delle metriche Polly (`resilience.polly.strategy.events`, `resilience.polly.strategy.attempt.duration`, `resilience.polly.pipeline.duration`) con i tag (`pipeline.name`, `pipeline.instance`, `strategy.name`, `event.name`, `event.severity`, `operation.key`, `exception.type`, `attempt.number`, `attempt.handled`) e i tag aggiunti dall'enricher Microsoft; come leggere una traccia (span di step → span HTTP per tentativo → span server di FlakyApi; eventi `polly.*`).
7. **Riferimenti** — link a learn.microsoft.com (resilience, http-resilience), pollydocs.org (strategies, chaos, telemetry), opentelemetry.io/docs/languages/dotnet.

- [x] **Step 4: Final verification pass**

Run all six examples once more in sequence (FlakyApi running) and confirm each ends with `✔ Scenario complete.` and exit code 0 (`$LASTEXITCODE`). Confirm `git status`-style cleanliness is not required (no repo). Confirm `src/Example04.DynamicReload/bin/.../appsettings.json` and `src/Example06.Chaos/bin/.../appsettings.json` are back to their original content.
