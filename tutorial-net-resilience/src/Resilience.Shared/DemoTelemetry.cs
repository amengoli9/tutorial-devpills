using System.Diagnostics;

namespace Resilience.Shared;

/// <summary>The one ActivitySource used by every demo scenario (each step = one root span).</summary>
public static class DemoTelemetry
{
    public const string ActivitySourceName = "Resilience.Demo";
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
}
