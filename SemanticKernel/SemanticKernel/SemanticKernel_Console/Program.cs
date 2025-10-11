using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.Ollama;
using Microsoft.SemanticKernel.Connectors.OpenAI;

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

builder.AddOllamaChatCompletion(modelId, new Uri(endpoint));


builder.Plugins
    .AddFromType<MyTimePlugin>();


Kernel kernel = builder.Build();
var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();

PromptExecutionSettings settings = new()
{
   FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
};

var history = new ChatHistory();
string? userInput;
do
{
   Console.Write("User > ");
   userInput = Console.ReadLine();

   if (userInput is null)
      break;

   history.AddUserMessage(userInput);

   Console.Write("Assistant > ");
   
   var response = chatCompletionService.GetStreamingChatMessageContentsAsync(
       history, 
       kernel: kernel,
       executionSettings: settings);

   var fullResponse = "";
   await foreach (var chunk in response)
   {
       Console.Write(chunk.Content);
       fullResponse += chunk.Content;
   }
   
   Console.WriteLine(); 
   
   history.AddAssistantMessage(fullResponse);
   
} while (userInput is not null);