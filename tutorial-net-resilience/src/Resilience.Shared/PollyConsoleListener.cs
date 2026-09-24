using Polly;
using Polly.Hedging;
using Polly.Retry;
using Polly.Simmy.Fault;
using Polly.Simmy.Latency;
using Polly.Simmy.Outcomes;
using Polly.Telemetry;

namespace Resilience.Shared;

/// <summary>
/// Prints every Polly telemetry event as one compact console line. Polly's own ILogger output is
/// complete but verbose (it includes full stack traces for exceptions), so the console logger
/// filters the "Polly" category out and this listener shows what happened instead. The full log
/// records still go to the OTLP exporter.
/// </summary>
public sealed class PollyConsoleListener : TelemetryListener
{
    public override void Write<TResult, TArgs>(in TelemetryEventArguments<TResult, TArgs> args)
    {
        // Pipeline start/end bookends add nothing the scenario line does not already say.
        if (args.Arguments is PipelineExecutingArguments or PipelineExecutedArguments)
        {
            return;
        }

        var outcome = args.Outcome is { } o ? Describe(o) : string.Empty;

        var line = args.Arguments switch
        {
            ExecutionAttemptArguments attempt =>
                $"attempt #{attempt.AttemptNumber + 1} → {outcome}{(attempt.Handled ? " (handled)" : string.Empty)} in {attempt.Duration.TotalMilliseconds:N0} ms",
            OnRetryArguments<TResult> retry =>
                $"{args.Event.EventName}: waiting {retry.RetryDelay.TotalMilliseconds:N0} ms before attempt #{retry.AttemptNumber + 2}",
            // OnHedging has no outcome: telemetry TResult is object, even for HTTP hedges.
            OnHedgingArguments<HttpResponseMessage> hedging =>
                $"{args.Event.EventName}: starting hedged attempt #{hedging.AttemptNumber + 2} in parallel",
            OnLatencyInjectedArguments latency =>
                $"{args.Event.EventName}: injecting +{latency.Latency.TotalMilliseconds:N0} ms",
            OnFaultInjectedArguments fault =>
                $"{args.Event.EventName}: injecting {fault.Fault.GetType().Name}",
            // Chaos.OnOutcome is reported without an outcome (TResult = object), so match the HTTP case explicitly.
            OnOutcomeInjectedArguments<HttpResponseMessage> injected =>
                $"{args.Event.EventName}: injecting {Describe(injected.Outcome)}",
            _ => $"{args.Event.EventName} {outcome}".TrimEnd(),
        };

        DemoNarrator.Polly($"[{args.Source.PipelineName}/{args.Source.StrategyName}] {line}");
    }

    private static string Describe<TResult>(Outcome<TResult> outcome)
    {
        if (outcome.Exception is { } exception)
        {
            return exception.GetType().Name;
        }

        return outcome.Result switch
        {
            null => "null",
            HttpResponseMessage response => $"HTTP {(int)response.StatusCode}",
            var result when result.GetType().Name == "VoidResult" => "ok",
            var result => result.ToString() ?? "ok",
        };
    }
}
