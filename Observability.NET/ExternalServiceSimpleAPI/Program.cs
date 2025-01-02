


using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
var serviceName = builder.Configuration["ServiceName"] ?? "PriceExample";
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
      .AddAspNetCoreInstrumentation()
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
builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
   app.UseSwagger();
   app.UseSwaggerUI();
}


app.UseAuthorization();


app.MapControllers();

app.Run();
