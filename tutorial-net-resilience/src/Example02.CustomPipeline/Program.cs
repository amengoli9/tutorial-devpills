using Example02.CustomPipeline;
using Resilience.Shared;

var builder = DemoHost.CreateBuilder(args, "Example02.CustomPipeline");
ResilienceConfig.Configure(builder.Services);

return await DemoHost.RunAsync(
    builder,
    "AddResilienceHandler(): compose fallback, retry, circuit breaker and timeout by hand.",
    Scenario.RunAsync);
