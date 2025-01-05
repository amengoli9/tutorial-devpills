using Microsoft.Extensions.Logging;
using OpenTelemetry.Trace;
using OpenTelemetryAspNetCore.Application;
using OpenTelemetryAspNetCore.Domain.Models;
using OpenTelemetryAspNetCore.Domain.Ports;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenTelemetryAspNetCore.Infrastructure.Adapters;

public class PriceRepository(ILogger<PriceRepository> logger, HttpClient httpClient) : IPriceRepository
{

   private static readonly JsonSerializerOptions _jsonOptions = new()
   {
      PropertyNameCaseInsensitive = true,
      NumberHandling = JsonNumberHandling.AllowReadingFromString
   };

   public async Task<Amount> GetPriceAsync(int id)
   {
      try
      {
         string url = $"http://localhost:5009/api/Price/{id}";
         logger.LogInformation("Calling API {Url}", url);
         HttpResponseMessage response = await httpClient.GetAsync(url);
         response.EnsureSuccessStatusCode();

         string jsonResponse = await response.Content.ReadAsStringAsync();
         return JsonSerializer.Deserialize<Amount>(jsonResponse, _jsonOptions);
      }
      catch (Exception ex)
      {
         logger.LogError(ex.Message, ex);
         Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.Message);
         Activity.Current?.AddException(ex);
         throw new Exception($"Errore nella deserializzazione della risposta: {ex.Message}");
      }

   }
}
