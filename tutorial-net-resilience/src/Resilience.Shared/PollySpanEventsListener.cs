using System.Diagnostics;
using Polly.Hedging;
using Polly.Retry;
using Polly.Telemetry;

namespace Resilience.Shared;

/// <summary>
/// Polly has metrics and logs but no tracing. This listener receives every Polly telemetry event
/// (OnRetry, OnCircuitOpened, OnHedging, OnTimeout, OnFallback, chaos injections, ...) and adds it as
/// an ActivityEvent on the current span, so a trace viewer shows *when* each resilience decision
/// happened next to the per-attempt HTTP spans.
/// </summary>
public sealed class PollySpanEventsListener : TelemetryListener
{
    public override void Write<TResult, TArgs>(in TelemetryEventArguments<TResult, TArgs> args)
    {
        var activity = Activity.Current;
        if (activity is null)
        {
            return;
        }

        var tags = new ActivityTagsCollection
        {
            { "polly.pipeline", args.Source.PipelineName },
            { "polly.strategy", args.Source.StrategyName },
            { "polly.event", args.Event.EventName },
            { "polly.severity", args.Event.Severity.ToString() },
            { "polly.operation_key", args.Context.OperationKey },
        };

        switch (args.Arguments)
        {
            case OnRetryArguments<TResult> retry:
                tags["polly.attempt"] = retry.AttemptNumber;
                tags["polly.retry_delay_ms"] = retry.RetryDelay.TotalMilliseconds;
                break;
            // Hedging numbers only the extra attempts, starting at zero. Include the original
            // attempt so this tag agrees with ExecutionAttempt's zero-based numbering.
            case OnHedgingArguments<HttpResponseMessage> hedging:
                tags["polly.attempt"] = hedging.AttemptNumber + 1;
                break;
            case ExecutionAttemptArguments attempt:
                tags["polly.attempt"] = attempt.AttemptNumber;
                tags["polly.handled"] = attempt.Handled;
                tags["polly.duration_ms"] = attempt.Duration.TotalMilliseconds;
                break;
        }

        if (args.Outcome is { } outcome)
        {
            if (outcome.Exception is not null)
            {
                tags["polly.exception"] = outcome.Exception.GetType().Name;
            }
            else if (outcome.Result is HttpResponseMessage response)
            {
                tags["polly.http.status"] = (int)response.StatusCode;
            }
        }

        activity.AddEvent(new ActivityEvent($"polly.{args.Event.EventName}", tags: tags));
    }
}
