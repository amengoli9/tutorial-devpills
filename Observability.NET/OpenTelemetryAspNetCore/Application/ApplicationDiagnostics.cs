using System.Diagnostics;

namespace OpenTelemetryAspNetCore.Application;

public static class ApplicationDiagnostics
{
   
   public static readonly string OrdersSourceName = "Orders.Processing";
   private static readonly string ServiceVersion = "1.0.0";

   public static readonly ActivitySource ActivitySource = new(OrdersSourceName, ServiceVersion);



}