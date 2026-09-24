namespace Resilience.Shared;

public static class FlakyApiEndpoints
{
    /// <summary>Region "eu-west": answers immediately.</summary>
    public const string Primary = "http://localhost:5100";

    /// <summary>Region "eu-east": same API, but every request gets +2500 ms of latency.</summary>
    public const string SlowRegion = "http://localhost:5101";
}
