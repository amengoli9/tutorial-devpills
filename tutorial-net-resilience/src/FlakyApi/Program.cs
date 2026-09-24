using System.Collections.Concurrent;
using FlakyApi;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
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
