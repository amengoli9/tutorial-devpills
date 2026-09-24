namespace FlakyApi;

/// <summary>Behavior of one "region" (= one listening port).</summary>
public sealed class RegionOptions
{
    public string Name { get; set; } = "unknown";
    public int ExtraLatencyMs { get; set; }
}
