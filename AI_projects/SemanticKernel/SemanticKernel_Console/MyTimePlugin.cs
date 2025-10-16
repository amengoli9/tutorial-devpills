// Copyright (c) Microsoft. All rights reserved.

using System;
using System.ComponentModel;
using Microsoft.SemanticKernel;


/// <summary>
/// Simple plugin that just returns the time.
/// </summary>
public class MyTimePlugin
{
   [KernelFunction, Description("Get the current time")]
   public DateTimeOffset Time() => DateTimeOffset.Now;
}



public class MyHttpMessageHandler : HttpClientHandler
{
   protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
   {
      if (request.RequestUri != null && request.RequestUri.Host.Equals("api.openai.com", StringComparison.OrdinalIgnoreCase))
      {
         request.RequestUri = new Uri($"http://localhost:1234{request.RequestUri.PathAndQuery}");
      }

      return base.SendAsync(request, cancellationToken);
   }
}
