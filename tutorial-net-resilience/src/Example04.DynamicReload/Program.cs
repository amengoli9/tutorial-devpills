using Example04.DynamicReload;
using Resilience.Shared;

var builder = DemoHost.CreateBuilder(args, "Example04.DynamicReload");
ResilienceConfig.Configure(builder.Services, builder.Configuration);

return await DemoHost.RunAsync(
    builder,
    "EnableReloads(): change retry settings at runtime by editing appsettings.json.",
    Scenario.RunAsync);
