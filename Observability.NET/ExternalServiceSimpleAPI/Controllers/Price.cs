namespace ExternalServiceSimpleAPI.Controllers;

public record Price()
{
   public string Currency { get; set; }
   public decimal Value { get; set; }
}
