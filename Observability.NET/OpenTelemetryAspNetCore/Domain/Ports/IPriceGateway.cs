using OpenTelemetryAspNetCore.Domain.Models;

namespace OpenTelemetryAspNetCore.Domain.Ports;

public interface IPriceGateway
{
   public Task<Price> GetPriceAsync(int id);
}
