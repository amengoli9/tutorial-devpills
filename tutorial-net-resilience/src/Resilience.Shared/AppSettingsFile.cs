using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Options;

namespace Resilience.Shared;

/// <summary>
/// Edits the appsettings.json that the running process loaded (the copy in the output folder),
/// so "reloadOnChange" configuration kicks in without restarting the app.
/// </summary>
public static class AppSettingsFile
{
    public static string Path => System.IO.Path.Combine(AppContext.BaseDirectory, "appsettings.json");

    public static string ReadAll() => File.ReadAllText(Path);

    public static void WriteAll(string content) => File.WriteAllText(Path, content);

    /// <summary>Whether editing this file can change the effective value (no higher-priority override).</summary>
    public static bool CanEditValue(IConfiguration configuration, string key)
    {
        if (configuration is not IConfigurationRoot root)
        {
            return false;
        }

        var provider = root.Providers.LastOrDefault(provider => provider.TryGet(key, out _));
        return provider is JsonConfigurationProvider json
            && string.Equals(json.Source.FileProvider?.GetFileInfo(json.Source.Path!).PhysicalPath,
                Path, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    /// <summary>Sets a value addressed with configuration syntax, e.g. "Resilience:Retry:MaxRetryAttempts".</summary>
    public static void SetValue(string colonPath, JsonNode value)
    {
        var root = JsonNode.Parse(ReadAll())!.AsObject();
        var segments = colonPath.Split(':');
        var current = root;
        for (var i = 0; i < segments.Length - 1; i++)
        {
            current = current[segments[i]]?.AsObject()
                ?? throw new InvalidOperationException($"Section '{segments[i]}' not found in {Path}");
        }

        current[segments[^1]] = value;
        WriteAll(root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Waits until the options monitor reports a value satisfying <paramref name="predicate"/>.</summary>
    public static async Task<TOptions> WaitForOptionsAsync<TOptions>(
        IOptionsMonitor<TOptions> monitor,
        string? name,
        Func<TOptions, bool> predicate,
        TimeSpan timeout)
    {
        var optionsName = name ?? Options.DefaultName;
        var tcs = new TaskCompletionSource<TOptions>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = monitor.OnChange((options, changedName) =>
        {
            if ((changedName ?? Options.DefaultName) == optionsName && predicate(options))
            {
                tcs.TrySetResult(options);
            }
        });

        var current = monitor.Get(optionsName);
        if (predicate(current))
        {
            return current;
        }

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(timeout));
        if (completed != tcs.Task)
        {
            throw new TimeoutException($"Options '{typeof(TOptions).Name}'/'{optionsName}' did not reload within {timeout}.");
        }

        return await tcs.Task;
    }
}
