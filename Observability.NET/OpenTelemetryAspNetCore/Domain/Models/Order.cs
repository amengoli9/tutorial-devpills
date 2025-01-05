namespace OpenTelemetryAspNetCore.Domain.Models;

public class Order
{
   public int Id { get; set; }
   public DateTime OrderDate { get; set; }

   public Amount Amount { get; set; } = new();
}
