using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;

namespace Resilience.Shared;

/// <summary>
/// Thin wrapper over an HttpClient that talks to FlakyApi. It never throws on non-2xx: the status
/// code is part of the result so the narrator can print "500 on attempt 1, 200 on attempt 3".
/// Resilience exceptions (BrokenCircuitException, TimeoutRejectedException, ...) do propagate.
/// </summary>
public sealed class FlakyApiClient(HttpClient http)
{
    public Task<ApiResult> OkAsync(CancellationToken ct = default)
        => SendAsync(HttpMethod.Get, "/ok", ct);

    public Task<ApiResult> FailThenOkAsync(int failures, string key, CancellationToken ct = default)
        => SendAsync(HttpMethod.Get, $"/fail-then-ok?failures={failures}&key={Uri.EscapeDataString(key)}", ct);

    public Task<ApiResult> UnstableAsync(double failRate, CancellationToken ct = default)
        => SendAsync(HttpMethod.Get, $"/unstable?failRate={failRate.ToString(CultureInfo.InvariantCulture)}", ct);

    public Task<ApiResult> SlowAsync(int ms, CancellationToken ct = default)
        => SendAsync(HttpMethod.Get, $"/slow?ms={ms}", ct);

    public Task<ApiResult> StatusAsync(int code, int? retryAfterSeconds = null, CancellationToken ct = default)
        => SendAsync(HttpMethod.Get, retryAfterSeconds is null ? $"/status/{code}" : $"/status/{code}?retryAfter={retryAfterSeconds}", ct);

    public Task<ApiResult> CreateOrderAsync(CancellationToken ct = default)
        => SendAsync(HttpMethod.Post, "/orders", ct);

    public async Task<bool> IsReachableAsync(CancellationToken ct = default)
    {
        try
        {
            return (await OkAsync(ct)).IsSuccess;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // HttpClient's probe timeout means unreachable; caller cancellation still propagates.
            return false;
        }
    }

    private async Task<ApiResult> SendAsync(HttpMethod method, string path, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        using var request = new HttpRequestMessage(method, path);
        if (method == HttpMethod.Post)
        {
            request.Content = JsonContent.Create(new { item = "demo", quantity = 1 });
        }

        using var response = await http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        stopwatch.Stop();

        response.Headers.TryGetValues("X-Region", out var regions);
        return new ApiResult((int)response.StatusCode, regions?.FirstOrDefault(), body, stopwatch.Elapsed);
    }
}
