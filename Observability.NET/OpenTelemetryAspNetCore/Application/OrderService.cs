using OpenTelemetry.Trace;
using OpenTelemetryAspNetCore.Domain.Models;
using OpenTelemetryAspNetCore.Domain.Ports;
using System.Diagnostics;

namespace OpenTelemetryAspNetCore.Application;


public class OrderService(ILogger<OrderService> logger, IPriceGateway priceGateway) : IOrderService
{
   private readonly List<Order> _orders = new List<Order> {
      new Order { Id = 1, TotalAmount = 100 },
   };

   public async Task<IEnumerable<Order>> GetAllAsync()
   {
      using var activity = ApplicationDiagnostics.ActivitySource.StartActivity("OrderService.GetAllAsync");
      activity?.SetTag("orders.count", _orders.Count);
      logger.LogInformation("Getting all orders");
      Price price = new Price();
      try
      {
         price = await priceGateway.GetPriceAsync(1);
         var a = price;
      }
      catch (Exception ex)
      {
         logger.LogError(ex.Message, ex);
         Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.Message);
         Activity.Current?.RecordException(ex);
      }
      _orders.ForEach(async o =>
      {
         o.Price = price;
         

      });

      return _orders;
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
