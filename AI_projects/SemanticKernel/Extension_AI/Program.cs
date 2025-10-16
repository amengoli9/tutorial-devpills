using Microsoft.Extensions.AI;
using OllamaSharp;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
var resourceBuilder = ResourceBuilder
    .CreateDefault()
    .AddService("TelemetryConsoleQuickstart");
// Configure OpenTelemetry exporter.
string sourceName = "test";
TracerProvider tracerProvider = OpenTelemetry.Sdk.CreateTracerProviderBuilder()

    .SetResourceBuilder(resourceBuilder)
    .AddSource(sourceName)
    .AddOtlpExporter()
    .AddConsoleExporter()
    .Build();

IChatClient ollamaClient = new OllamaApiClient(
    new Uri("http://localhost:11434/"), "llama3.1");

IChatClient client = new ChatClientBuilder(ollamaClient)
    .UseOpenTelemetry(
        sourceName: sourceName,
        configure: c => c.EnableSensitiveData = true)
    .Build();

Console.WriteLine((await client.GetResponseAsync("What is AI?")).Text);