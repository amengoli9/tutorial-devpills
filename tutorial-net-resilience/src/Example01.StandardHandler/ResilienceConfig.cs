using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Resilience.Shared;

// RemoveAllResilienceHandlers() is still marked [Experimental] in Microsoft.Extensions.Http.Resilience 10.x:
// the API works and is documented, but its shape may change. Opting in explicitly keeps
// TreatWarningsAsErrors on for everything else.
#pragma warning disable EXTEXP0001

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
