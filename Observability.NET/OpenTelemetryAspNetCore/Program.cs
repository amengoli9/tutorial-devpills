using Microsoft.AspNetCore.Mvc;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetryAspNetCore.Application;
using OpenTelemetryAspNetCore.Domain.Ports;
using OpenTelemetryAspNetCore.Infrastructure.Adapters;
using System.Globalization;

var builder = WebApplication.CreateBuilder(args); 
builder.Services.AddSwaggerGen();
builder.Services.AddEndpointsApiExplorer();

var serviceName = builder.Configuration["ServiceName"] ?? "OpenTelemetryAspNetCore";
var serviceVersion = builder.Configuration["ServiceVersion"] ?? "1.0.0"; // only for demo purposes

builder.Services.AddOpenTelemetry()
   .ConfigureResource(resource => resource.AddService(serviceName, serviceVersion)
        .AddAttributes(new Dictionary<string, object>
        {
           ["deployment.environment"] = builder.Environment.EnvironmentName,
           ["host.name"] = Environment.MachineName,
           ["service.instance.id"] = Guid.NewGuid().ToString()
        }))
   .WithTracing(trace => trace
      .AddSource(ApplicationDiagnostics.OrdersSourceName)
       .AddAspNetCoreInstrumentation(options =>
       {
          options.Filter = context => !context.Request.Path.Equals("/health");
       })
      .AddHttpClientInstrumentation()
      .AddConsoleExporter()
      .AddOtlpExporter()
      .AddOtlpExporter(c => c.Endpoint = new Uri("http://localhost:4318")))
   .WithMetrics(metrics => metrics
      .AddAspNetCoreInstrumentation()
      .AddRuntimeInstrumentation()
      .AddHttpClientInstrumentation()
      .AddConsoleExporter()
      .AddOtlpExporter())
   .WithLogging(logger => logger
      .AddConsoleExporter()
      .AddOtlpExporter());

builder.Services.AddHealthChecks();

builder.Services.AddHttpClient(); 
builder.Services.AddScoped<IPriceGateway, PriceGateway>();
builder.Services.AddScoped<IOrderService, OrderService>();

var app = builder.Build();

app.MapHealthChecks("/health");

app.MapOrderEndpoints();
if (app.Environment.IsDevelopment())
{
   app.UseSwagger();
   app.UseSwaggerUI();
}


app.Run();


