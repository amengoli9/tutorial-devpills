using Example01.StandardHandler;
using Resilience.Shared;

var builder = DemoHost.CreateBuilder(args, "Example01.StandardHandler");
ResilienceConfig.Configure(builder.Services, builder.Configuration);

return await DemoHost.RunAsync(
    builder,
    "AddStandardResilienceHandler(): the recommended default for every HttpClient.",
    Scenario.RunAsync);
