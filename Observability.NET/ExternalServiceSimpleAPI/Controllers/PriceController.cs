using Microsoft.AspNetCore.Mvc;

namespace ExternalServiceSimpleAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PriceController(ILogger<PriceController> logger) : ControllerBase
{
   private static readonly string[] Summaries = new[]
   {
        "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
    };


   [HttpGet("{id}")]
   public async Task<Price> GetAsync(int id)
   {
      var toReturn = new Price
      {
         Currency = "USD",
         Value = Random.Shared.Next(1, 55) * id * 1.5m
      };
      logger.LogInformation("Returning price {Price}", toReturn);
      return toReturn;

   }

}

