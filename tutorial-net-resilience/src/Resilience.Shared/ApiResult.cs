namespace Resilience.Shared;

/// <summary>What a scenario needs to narrate an HTTP call: status, which region answered, how long it took.</summary>
public sealed record ApiResult(int StatusCode, string? Region, string Body, TimeSpan Elapsed)
{
    public bool IsSuccess => StatusCode is >= 200 and < 300;

    public override string ToString()
        => $"HTTP {StatusCode} from '{Region ?? "?"}' in {Elapsed.TotalMilliseconds:N0} ms";
}
