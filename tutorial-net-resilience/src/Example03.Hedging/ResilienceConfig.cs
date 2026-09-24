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
