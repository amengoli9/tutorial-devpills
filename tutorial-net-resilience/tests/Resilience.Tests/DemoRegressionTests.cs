using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Hedging;
using Polly.Telemetry;
using Resilience.Shared;
using Xunit;
using ChaosConfig = Example06.Chaos.ResilienceConfig;
using ChaosOptions = Example06.Chaos.ChaosOptions;

// Console narration and DemoNarrator.ExampleName are process-wide demo state.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Resilience.Tests;

public sealed class DemoRegressionTests
{
    [Fact]
    public async Task StartupProbeOptsOutOfLaterGlobalResilienceDefaults()
    {
        var builder = DemoHost.CreateBuilder([], "ProbeTest");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Resilience:Standard:Retry:MaxRetryAttempts"] = "1",
        });
        Example01.StandardHandler.ResilienceConfig.Configure(builder.Services, builder.Configuration);
        var attempts = 0;
        builder.Services.AddHttpClient(DemoHost.RawClientName).ConfigurePrimaryHttpMessageHandler(() =>
            new StubHandler(_ =>
            {
                attempts++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            }));
        using var host = builder.Build();
        using var http = host.Services.GetRequiredService<IHttpClientFactory>().CreateClient(DemoHost.RawClientName);

        Assert.Equal(TimeSpan.FromSeconds(2), http.Timeout);
        Assert.False(await new FlakyApiClient(http).IsReachableAsync());
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task ProbeTimeoutMeansUnreachableButCallerCancellationPropagates()
    {
        using var http = new HttpClient(new StubHandler(async ct =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        })) { BaseAddress = new Uri("http://localhost"), Timeout = TimeSpan.FromMilliseconds(50) };
        var client = new FlakyApiClient(http);

        Assert.False(await client.IsReachableAsync());
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.IsReachableAsync(cancelled.Token));
    }

    [Fact]
    public async Task HttpHedgeEmitsAttemptDetailsToConsoleAndSpan()
    {
        var telemetry = new TelemetryOptions();
        telemetry.TelemetryListeners.Add(new PollyConsoleListener());
        telemetry.TelemetryListeners.Add(new PollySpanEventsListener());
        var pipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddHedging(new HedgingStrategyOptions<HttpResponseMessage>
            {
                Delay = TimeSpan.Zero,
                MaxHedgedAttempts = 1,
            })
            .ConfigureTelemetry(telemetry)
            .Build();
        using var activity = new Activity("hedging-test").Start();
        using var output = new StringWriter();
        var originalOutput = Console.Out;
        var attempts = 0;
        try
        {
            Console.SetOut(output);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var response = await pipeline.ExecuteAsync(async ct =>
            {
                if (Interlocked.Increment(ref attempts) == 1)
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                }

                return new HttpResponseMessage(HttpStatusCode.OK);
            }, deadline.Token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally
        {
            Console.SetOut(originalOutput);
        }

        Assert.Equal(2, attempts);
        Assert.Contains("starting hedged attempt #2", output.ToString());
        var hedge = Assert.Single(activity.Events, e => e.Name == "polly.OnHedging");
        Assert.Equal(1, hedge.Tags.Single(tag => tag.Key == "polly.attempt").Value);
    }

    [Fact]
    public async Task ProductionForcesChaosOffEvenAfterConfigurationReload()
    {
        var builder = DemoHost.CreateBuilder(["--environment", "Production"], "ProductionTest");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Chaos:Enabled"] = "true",
            ["Chaos:LatencyInjectionRate"] = "0",
            ["Chaos:FaultInjectionRate"] = "1",
            ["Chaos:OutcomeInjectionRate"] = "0",
        });
        ChaosConfig.Configure(builder.Services, builder.Configuration);
        builder.Services.AddHttpClient(ChaosConfig.ClientName).ConfigurePrimaryHttpMessageHandler(() =>
            new StubHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))));
        using var host = builder.Build();
        var monitor = host.Services.GetRequiredService<IOptionsMonitor<ChaosOptions>>();
        using var http = host.Services.GetRequiredService<IHttpClientFactory>().CreateClient(ChaosConfig.ClientName);

        Assert.False(monitor.CurrentValue.Enabled);
        using var before = await http.GetAsync("/ok");
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        ((IConfigurationRoot)builder.Configuration).Reload();
        Assert.False(monitor.CurrentValue.Enabled);
        using var after = await http.GetAsync("/ok");
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
    }

    [Theory]
    [InlineData("Production", "true")]
    [InlineData("Development", "false")]
    public async Task ChaosScenarioRespectsOverridesWithoutWaitingForFileChanges(string environment, string enabled)
    {
        var builder = DemoHost.CreateBuilder(
            ["--environment", environment, "--Chaos:Enabled", enabled], "OverrideTest");
        ChaosConfig.Configure(builder.Services, builder.Configuration);
        var calls = 0;
        builder.Services.AddHttpClient(ChaosConfig.ClientName).ConfigurePrimaryHttpMessageHandler(() =>
            new StubHandler(_ =>
            {
                calls++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            }));
        using var host = builder.Build();
        var originalJson = AppSettingsFile.ReadAll();

        Assert.False(AppSettingsFile.CanEditValue(builder.Configuration, "Chaos:Enabled"));
        await Example06.Chaos.Scenario.RunAsync(host.Services, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(10, calls);
        Assert.Equal(originalJson, AppSettingsFile.ReadAll());
    }

    private sealed class StubHandler(Func<CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => send(cancellationToken);
    }
}
