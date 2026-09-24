namespace Example06.Chaos;

/// <summary>Chaos knobs, bound from "Chaos" in appsettings.json. Keep Enabled=false in Production.</summary>
public sealed class ChaosOptions
{
    public bool Enabled { get; set; }
    public double LatencyInjectionRate { get; set; } = 0.3;
    public int LatencyMs { get; set; } = 2000;
    public double FaultInjectionRate { get; set; } = 0.2;
    public double OutcomeInjectionRate { get; set; } = 0.3;
}
