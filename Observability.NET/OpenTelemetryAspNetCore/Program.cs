using Microsoft.AspNetCore.Mvc;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetryAspNetCore.Application;
using OpenTelemetryAspNetCore.Domain.Ports;
using System.Globalization;

var builder = WebApplication.CreateBuilder(args);
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
      .AddSource(serviceName)
      .AddAspNetCoreInstrumentation()
      .AddConsoleExporter()
      .AddOtlpExporter())
   .WithMetrics(metrics => metrics
      .AddAspNetCoreInstrumentation()
      .AddRuntimeInstrumentation()
      .AddHttpClientInstrumentation()
      .AddConsoleExporter()
      .AddOtlpExporter())
   .WithLogging(logger => logger
      .AddConsoleExporter()
      .AddOtlpExporter());


builder.Services.AddScoped<IOrderService, OrderService>();

var app = builder.Build();


app.MapOrderEndpoints();
app.Run();


