using OpenTelemetryAspNetCore.Domain.Models;
using OpenTelemetryAspNetCore.Domain.Ports;

namespace OpenTelemetryAspNetCore.Application;


public class OrderService(ILogger<OrderService> logger) : IOrderService
{
    private readonly List<Order> _orders = new();

    public Task<IEnumerable<Order>> GetAllAsync()
    {
        logger.LogInformation("Getting all orders");
        return Task.FromResult(_orders.AsEnumerable());
    }

    public Task<Order?> GetByIdAsync(int id) =>
        Task.FromResult(_orders.FirstOrDefault(o => o.Id == id));

    public Task<Order> CreateAsync(Order order)
    {
        order.Id = _orders.Count + 1;
        order.OrderDate = DateTime.UtcNow;
        _orders.Add(order);
        logger.LogInformation("Order {OrderId} created", order.Id);
        return Task.FromResult(order);
    }

    public Task<Order?> UpdateAsync(int id, Order order)
    {
        var index = _orders.FindIndex(o => o.Id == id);
        if (index == -1) return Task.FromResult<Order?>(null);

        order.Id = id;
        _orders[index] = order;
        return Task.FromResult<Order?>(order);
    }

    public Task<bool> DeleteAsync(int id)
    {
        var index = _orders.FindIndex(o => o.Id == id);
        if (index == -1) return Task.FromResult(false);

        _orders.RemoveAt(index);
        return Task.FromResult(true);
    }
}