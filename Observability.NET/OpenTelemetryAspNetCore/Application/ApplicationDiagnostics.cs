using System.Diagnostics;

namespace OpenTelemetryAspNetCore.Application;

public static class ApplicationDiagnostics
{
   
   private static readonly string ServiceName = "OpenTelemetryAspNetCore";
   private static readonly string ServiceVersion = "1.0.0";

   public static readonly ActivitySource ActivitySource = new(ServiceName, ServiceVersion);



}