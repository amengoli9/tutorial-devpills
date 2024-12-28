using Microsoft.AspNetCore.Mvc;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Globalization;

var builder = WebApplication.CreateBuilder(args);

var serviceName = builder.Configuration["ServiceName"] ?? "MyService";
var serviceVersion = builder.Configuration["ServiceVersion"] ?? "1.0.0"; // only for demo purposes

builder.Services.AddOpenTelemetry()
   .ConfigureResource(resource => resource
        .AddService(serviceName, serviceVersion))
   .WithTracing(trace => trace
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



var app = builder.Build();

app.MapGet("/", () => "Hello World!");
app.MapGet("/example/{exampleString?}", DemoLogic);
app.Run();



bool DemoLogic([FromServices] ILogger<Program> logger, string? exampleString)
{
   bool result = true;
   if (string.IsNullOrEmpty(exampleString))
   {
      logger.LogWarning("ExampleString is missing!");
      result = false;
   }
   else
   {
      logger.LogInformation("Called by {ExampleString}", exampleString);
   }

   return result;
}