using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetryAspNetCore.Application;
using OpenTelemetryAspNetCore.Domain.Ports;
using OpenTelemetryAspNetCore.Infrastructure.Adapters;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

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
      .AddOtlpExporter(c => c.Endpoint = new Uri("http://localhost:4318"))
      .AddOtlpExporter(opts =>
      {
         opts.Endpoint = new Uri("http://localhost:5341/ingest/otlp/v1/traces");
         opts.Protocol = OtlpExportProtocol.HttpProtobuf;
      }))
   .WithMetrics(metrics => metrics
      .AddAspNetCoreInstrumentation()
      .AddRuntimeInstrumentation()
      .AddHttpClientInstrumentation()
      .AddConsoleExporter()
      .AddOtlpExporter())
   .WithLogging(logger => logger

      .AddConsoleExporter()
      .AddOtlpExporter()
      .AddOtlpExporter(c => c.Endpoint = new Uri("http://localhost:4318"))
      .AddOtlpExporter(c => { c.Endpoint = new Uri("http://localhost:5341/ingest/otlp/v1/logs"); c.Protocol = OtlpExportProtocol.HttpProtobuf; }
      ))
   ;


//NEEDED TO ADD SCOPE TO LOG
builder.Logging.ClearProviders();
builder.Logging.AddOpenTelemetry(opt =>
{
   opt.IncludeFormattedMessage = true;
   opt.IncludeScopes = true;
   opt.AddOtlpExporter();
   opt.AddOtlpExporter(c => c.Endpoint = new Uri("http://localhost:4318"));
   opt.AddOtlpExporter(c => { c.Endpoint = new Uri("http://localhost:5341/ingest/otlp/v1/logs"); c.Protocol = OtlpExportProtocol.HttpProtobuf; });

   opt.AddConsoleExporter();
}
);

builder.Services.AddHealthChecks();

builder.Services.AddHttpClient(); 
builder.Services.AddScoped<IPriceRepository, PriceRepository>();
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

