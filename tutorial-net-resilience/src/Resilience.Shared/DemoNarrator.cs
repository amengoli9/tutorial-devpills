using System.Diagnostics;
using System.Text;
using Polly.CircuitBreaker;
using Polly.RateLimiting;
using Polly.Timeout;

namespace Resilience.Shared;

/// <summary>Console narration: numbered steps (each one is also a root span), colored outcomes.</summary>
public static class DemoNarrator
{
    private static int _stepNumber;

    public static string ExampleName { get; set; } = "Example";

    public static void Title(string exampleName, string description)
    {
        Console.OutputEncoding = Encoding.UTF8;
        ExampleName = exampleName;
        Console.WriteLine();
        Write(ConsoleColor.White, $"=== {exampleName} ===");
        Write(ConsoleColor.Gray, description);
    }

    public static DemoStep Step(string title, string why)
    {
        var number = Interlocked.Increment(ref _stepNumber);
        Console.WriteLine();
        Write(ConsoleColor.Cyan, $"── Step {number}: {title}");
        Write(ConsoleColor.DarkGray, $"   Why: {why}");

        var activity = DemoTelemetry.ActivitySource.StartActivity($"{ExampleName} / step {number}: {title}");
        activity?.SetTag("demo.example", ExampleName);
        activity?.SetTag("demo.step", number);
        activity?.SetTag("demo.step.title", title);
        return new DemoStep(activity);
    }

    public static void Info(string message) => Write(ConsoleColor.Gray, $"   {message}");
    public static void Ok(string message) => Write(ConsoleColor.Green, $"   ✔ {message}");
    public static void Fail(string message) => Write(ConsoleColor.Yellow, $"   ✖ {message}");
    public static void Note(string message) => Write(ConsoleColor.Magenta, $"   ↳ {message}");
    public static void Polly(string message) => Write(ConsoleColor.DarkCyan, $"   ⚙ polly {message}");

    /// <summary>
    /// Runs one HTTP call and prints the outcome. Resilience exceptions are *expected* outcomes in
    /// these demos, so they are printed, not rethrown. Anything else propagates.
    /// </summary>
    public static async Task<ApiResult?> CallAsync(Func<Task<ApiResult>> call)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await call();
            var line = $"{result} — {Truncate(result.Body)}";
            if (result.IsSuccess)
            {
                Ok(line);
            }
            else
            {
                Fail(line);
            }

            return result;
        }
        catch (BrokenCircuitException ex)
        {
            Fail($"{ex.GetType().Name} after {stopwatch.ElapsedMilliseconds} ms — {ex.Message}");
        }
        catch (TimeoutRejectedException ex)
        {
            Fail($"TimeoutRejectedException after {stopwatch.ElapsedMilliseconds} ms — {ex.Message}");
        }
        catch (RateLimiterRejectedException ex)
        {
            var hint = ex.RetryAfter is { } retryAfter ? $"retry after {retryAfter.TotalMilliseconds:N0} ms" : "no retry-after hint";
            Fail($"RateLimiterRejectedException after {stopwatch.ElapsedMilliseconds} ms — {hint}");
        }
        catch (HttpRequestException ex)
        {
            Fail($"HttpRequestException after {stopwatch.ElapsedMilliseconds} ms — {ex.Message}");
        }
        catch (TaskCanceledException ex)
        {
            Fail($"TaskCanceledException after {stopwatch.ElapsedMilliseconds} ms — {ex.Message}");
        }

        return null;
    }

    private static string Truncate(string body)
    {
        var flat = body.Replace('\n', ' ').Replace('\r', ' ');
        return flat.Length <= 90 ? flat : flat[..90] + "…";
    }

    private static readonly Lock ConsoleLock = new();

    private static void Write(ConsoleColor color, string text)
    {
        // Scenarios with parallel calls (bulkhead) print from several threads at once.
        lock (ConsoleLock)
        {
            var previous = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.WriteLine(text);
            Console.ForegroundColor = previous;
        }
    }
}

public sealed class DemoStep(Activity? activity) : IDisposable
{
    public Activity? Activity { get; } = activity;

    public void Dispose() => Activity?.Dispose();
}
