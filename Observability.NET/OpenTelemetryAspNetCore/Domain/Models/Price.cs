namespace OpenTelemetryAspNetCore.Domain.Models;

public class Price()
{
    public string Currency { get; set; } = "USD";
    public decimal Value { get; set; } = 0;
}
