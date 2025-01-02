using Microsoft.Extensions.Logging;
using OpenTelemetryAspNetCore.Domain.Models;
using OpenTelemetryAspNetCore.Domain.Ports;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenTelemetryAspNetCore.Infrastructure.Adapters;

public class PriceGateway(ILogger<PriceGateway> logger, HttpClient httpClient) : IPriceGateway
{

   private static readonly JsonSerializerOptions _jsonOptions = new()
   {
      PropertyNameCaseInsensitive = true,
      NumberHandling = JsonNumberHandling.AllowReadingFromString
   };

   public async Task<Price> GetPriceAsync(int id)
   {
      try
      {
         string url = $"http://localhost:5009/api/Price/{id}";
         logger.LogInformation("Calling API {Url}", url);
         HttpResponseMessage response = await httpClient.GetAsync(url);
         response.EnsureSuccessStatusCode();

         string jsonResponse = await response.Content.ReadAsStringAsync();
         return JsonSerializer.Deserialize<Price>(jsonResponse, _jsonOptions);
      }
      catch (HttpRequestException ex)
      {
         // Gestione errori di rete o HTTP
         throw new Exception($"Errore durante la chiamata API: {ex.Message}");
      }
      catch (JsonException ex)
      {
         // Gestione errori di deserializzazione
         throw new Exception($"Errore nella deserializzazione della risposta: {ex.Message}");
      }
      catch (Exception ex)
      {
         // Gestione errori di deserializzazione
         logger.LogError(ex.Message, ex);
         throw new Exception($"Errore nella deserializzazione della risposta: {ex.Message}");
      }

   }
}
