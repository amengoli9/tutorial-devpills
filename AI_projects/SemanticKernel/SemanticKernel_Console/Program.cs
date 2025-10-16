using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.Ollama;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using OllamaSharp;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Diagnostics;

//var modelId = "";
//var endpoint = "";
//var apiKey = "";
var modelId = "llama3.1"; 
var endpoint = "http://localhost:11434/"; 

// only for testing purposes, disable certificate validation
var handler = new HttpClientHandler();
handler.CheckCertificateRevocationList = false;
var client = new HttpClient(handler);

var builder = Kernel.CreateBuilder();
HttpClient clientk = new HttpClient(new MyHttpMessageHandler());

//builder.AddOllamaChatCompletion(modelId, new Uri(endpoint));

builder.AddOpenAIChatCompletion("fake-model-name", "fake-api-key", httpClient: clientk);
builder.Plugins
    .AddFromType<MyTimePlugin>();





// Crea un ActivitySource personalizzato per tracciare le attività della chat
var activitySource = new ActivitySource("ChatApplication");

var resourceBuilder = ResourceBuilder
    .CreateDefault()
    .AddService("TelemetryConsoleQuickstart");

// Enable model diagnostics with sensitive data.

AppContext.SetSwitch("Microsoft.SemanticKernel.Experimental.GenAI.EnableOTelDiagnostics", true);
AppContext.SetSwitch("Microsoft.SemanticKernel.Experimental.GenAI.EnableOTelDiagnosticsSensitive", true);

using var traceProvider = Sdk.CreateTracerProviderBuilder()
    .SetResourceBuilder(resourceBuilder)
    .AddSource("Microsoft.SemanticKernel*")
    .AddSource("Microsoft.Extensions.AI*")
    .AddSource("ChatApplication") // Aggiungi il nostro ActivitySource personalizzato
    .AddOtlpExporter()
    .Build();

using var meterProvider = Sdk.CreateMeterProviderBuilder()
    .SetResourceBuilder(resourceBuilder)
    .AddMeter("Microsoft.SemanticKernel*")
    .AddMeter("Microsoft.Extensions.AI*")
    .AddOtlpExporter()
    .Build();

using var loggerFactory = LoggerFactory.Create(builder =>
{
   // Add OpenTelemetry as a logging provider
   builder.AddOpenTelemetry(options =>
   {
      options.SetResourceBuilder(resourceBuilder);
      options.AddOtlpExporter();
      // Format log messages. This is default to false.
      options.IncludeFormattedMessage = true;
      options.IncludeScopes = true;
   });
   builder.SetMinimumLevel(LogLevel.Information);
});






Kernel kernel = builder.Build();
var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();

PromptExecutionSettings settings = new()
{
   FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
};

var history = new ChatHistory();
string? userInput;

// Avvia un'Activity per tracciare l'intera sessione di chat
using var chatSessionActivity = activitySource.StartActivity("ChatSession", ActivityKind.Server);
chatSessionActivity?.SetTag("session.id", Guid.NewGuid().ToString());
chatSessionActivity?.SetTag("application.name", "SemanticKernel Console Chat");



IChatClient ollamaClient = new OllamaApiClient(
    new Uri("http://localhost:11434/"), "llama3.1");

IChatClient client2 = new ChatClientBuilder(ollamaClient)
    .UseOpenTelemetry(
        sourceName: "ChatApplication",
        configure: c => c.EnableSensitiveData = true)
    .Build();

//Console.WriteLine((await client2.GetResponseAsync("What time is it?")).Text);

var messageCount = 0;

do
{
   Console.Write("User > ");
   userInput = Console.ReadLine();

   if (userInput is null)
      break;

   messageCount++;

   // Crea un'Activity per ogni singolo messaggio dell'utente
   using var userMessageActivity = activitySource.StartActivity("UserMessage", ActivityKind.Internal);
   userMessageActivity?.SetTag("message.number", messageCount);
   userMessageActivity?.SetTag("message.length", userInput.Length);
   userMessageActivity?.AddEvent(new ActivityEvent("UserInputReceived"));

   history.AddUserMessage(userInput);

   Console.Write("Assistant > ");
   
   // Crea un'Activity per la risposta dell'assistente
   using var assistantResponseActivity = activitySource.StartActivity("AssistantResponse", ActivityKind.Internal);
   assistantResponseActivity?.SetTag("message.number", messageCount);
   
   var response = chatCompletionService.GetStreamingChatMessageContentsAsync(
       history, 
       kernel: kernel,
       executionSettings: settings);

   var fullResponse = "";
   var chunkCount = 0;
   await foreach (var chunk in response)
   {
       Console.Write(chunk.Content);
       fullResponse += chunk.Content;
       chunkCount++;
   }
   
   assistantResponseActivity?.SetTag("response.chunks", chunkCount);
   assistantResponseActivity?.SetTag("response.length", fullResponse.Length);
   assistantResponseActivity?.AddEvent(new ActivityEvent("ResponseCompleted"));
   
   Console.WriteLine(); 
   
   history.AddAssistantMessage(fullResponse);
   
} while (userInput is not null);

chatSessionActivity?.SetTag("total.messages", messageCount);
chatSessionActivity?.AddEvent(new ActivityEvent("ChatSessionEnded"));