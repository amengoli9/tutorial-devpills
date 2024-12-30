using OpenTelemetryAspNetCore.Domain.Models;
using OpenTelemetryAspNetCore.Domain.Ports;

public static class OrderEndpoints
{
   public static void MapOrderEndpoints(this IEndpointRouteBuilder app)
   {
      app.MapGet("/api/orders", async (IOrderService service) =>
          await service.GetAllAsync());

      app.MapGet("/api/orders/{id}", async (int id, IOrderService service) =>
          await service.GetByIdAsync(id) is Order order
              ? Results.Ok(order)
              : Results.NotFound());

      app.MapPost("/api/orders", async (Order order, IOrderService service) =>
      {
         var result = await service.CreateAsync(order);
         return Results.Created($"/orders/{result.Id}", result);
      });

      app.MapPut("/api/orders/{id}", async (int id, Order order, IOrderService service) =>
          await service.UpdateAsync(id, order) is Order updatedOrder
              ? Results.Ok(updatedOrder)
              : Results.NotFound());

      app.MapDelete("/api/orders/{id}", async (int id, IOrderService service) =>
          await service.DeleteAsync(id)
              ? Results.Ok()
              : Results.NotFound());
   }
}