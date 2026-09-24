using Example05.NonHttpPipelines;
using Resilience.Shared;

var builder = DemoHost.CreateBuilder(args, "Example05.NonHttpPipelines");
ResilienceConfig.Configure(builder.Services);

return await DemoHost.RunAsync(
    builder,
    "AddResiliencePipeline(): retry, bulkhead and throttling for non-HTTP dependencies.",
    Scenario.RunAsync);
