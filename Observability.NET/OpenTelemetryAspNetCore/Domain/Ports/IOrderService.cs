using OpenTelemetryAspNetCore.Domain.Models;

namespace OpenTelemetryAspNetCore.Domain.Ports;

public interface IOrderService
{
   Task<IEnumerable<Order>> GetAllAsync();
   Task<Order?> GetByIdAsync(int id);
   Task<Order> CreateAsync(Order order);
   Task<Order?> UpdateAsync(int id, Order order);
   Task<bool> DeleteAsync(int id);
}
