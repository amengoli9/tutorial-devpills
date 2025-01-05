using OpenTelemetryAspNetCore.Domain.Models;

namespace OpenTelemetryAspNetCore.Domain.Ports;

public interface IPriceRepository
{
   public Task<Amount> GetPriceAsync(int id);
}
