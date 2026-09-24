using Example03.Hedging;
using Resilience.Shared;

var builder = DemoHost.CreateBuilder(args, "Example03.Hedging");
ResilienceConfig.Configure(builder.Services);

return await DemoHost.RunAsync(
    builder,
    "AddStandardHedgingHandler(): race a slow endpoint against a fast one.",
    Scenario.RunAsync);
