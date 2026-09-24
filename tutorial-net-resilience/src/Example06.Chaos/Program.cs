using Example06.Chaos;
using Resilience.Shared;

var builder = DemoHost.CreateBuilder(args, "Example06.Chaos");
ResilienceConfig.Configure(builder.Services, builder.Configuration);

return await DemoHost.RunAsync(
    builder,
    "Chaos strategies: inject latency, faults and fake results to prove the pipeline holds.",
    Scenario.RunAsync);
