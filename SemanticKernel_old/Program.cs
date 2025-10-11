// Import packages
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using System.Diagnostics;

// Populate values from your OpenAI deployment
//var modelId = "gpt-4.1";
//var endpoint = "https://austinsvedese.openai.azure.com/";
//var apiKey = "3a7534d534354bfe8f686a2ba53a08ba";
//var handler = new HttpClientHandler();
//handler.CheckCertificateRevocationList = false;
//var client = new HttpClient(handler);

var ollamaModelId = "llama3";
var ollamaEndpoint = new Uri("http://localhost:11434");


// Update the builder to use the correct client
//var builder = Kernel.CreateBuilder().AddAzureOpenAIChatCompletion(modelId, endpoint, apiKey, httpClient: client);

#pragma warning disable SKEXP0070 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
var builder = Kernel.CreateBuilder().AddOllamaChatCompletion(ollamaModelId, ollamaEndpoint);
#pragma warning restore SKEXP0070 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

// Add enterprise components
//builder.Services.AddLogging(services => services.AddConsole().SetMinimumLevel(LogLevel.Trace));


// Build the kernel
Kernel kernel = builder.Build();
var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();

// Add a plugin (the LightsPlugin class is defined below)
//kernel.Plugins.AddFromType<LightsPlugin>("Lights");
string GetCurrentWeather() => Random.Shared.NextDouble() > 0.5 ? "It's sunny" : "It's raining";


// Enable planning
OpenAIPromptExecutionSettings openAIPromptExecutionSettings = new()
{
   FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
};

// Create a history store the conversation
var history = new ChatHistory();



var chatService = kernel.GetRequiredService<IChatCompletionService>();

// Convert to IChatClient when needed
#pragma warning disable SKEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
IChatClient chatClient = chatService.AsChatClient();
// Or convert back
IChatCompletionService backToService = chatClient.AsChatCompletionService();

#pragma warning restore SKEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

// Initiate a back-and-forth chat
string? userInput;
do
{
   // Collect user input
   Console.Write("User > ");
   userInput = Console.ReadLine();

   // Add user input
   history.AddUserMessage(userInput);
   ChatOptions options = new ChatOptions();
   // Get the response from the AI
   //var result = await 
      
   //   chatCompletionService
   //   //.GetStreamingResponseAsync(history,options: )
      
      
   //   .GetChatMessageContentAsync(
   //    history,
   //    executionSettings: openAIPromptExecutionSettings,
   //    kernel: kernel);



   bool roleWritten = false;
   string fullMessage = string.Empty;
   List<StreamingChatMessageContent> chatUpdates = [];
   await foreach (var chatUpdate in chatCompletionService.GetStreamingChatMessageContentsAsync(history, executionSettings: openAIPromptExecutionSettings,
kernel: kernel))
   {
      chatUpdates.Add(chatUpdate);

      Console.Write(chatUpdate);
   }

   Console.WriteLine("\n------------------------");
   //history.AddRange(chatUpdates.ToChatResponse().Messages);
   // Print the results
   //Console.WriteLine("Assistant > " + result);

   // Add the message from the agent to the chat history
   //history.AddMessage(result.Role, result.Content ?? string.Empty);
} while (userInput is not null);




