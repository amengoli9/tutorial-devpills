# Design: `tutorial-net-resilience` — Microsoft.Extensions.Resilience demo with OpenTelemetry

Date: 2026-09-21
Status: draft for review

## 1. Goal

A DevPills tutorial that shows the current (Sept 2026) feature set of
`Microsoft.Extensions.Resilience` / `Microsoft.Extensions.Http.Resilience` (10.10.0, built on
Polly 8.8.0), one focused example per project, every process instrumented with OpenTelemetry
(traces, metrics, logs) exported over **OTLP only**. The reader runs a collector of their choice;
nothing in the repo depends on Aspire, Seq, or any specific backend.

Each example is a **scripted, narrated console scenario**: it prints what it is about to do and
why, executes the calls against a deliberately unreliable API, prints the outcome, and exits.

Audience: .NET developers watching a DevPill. README in Italian; code comments in English.

## 2. Non-goals

- No Aspire (dashboard, AppHost, ServiceDefaults) — explicitly excluded by the user.
- No console/Seq/other exporters; OTLP is the only exporter.
- No automated test project. The deliverable is a demo; correctness is verified by building and
  running every scenario end-to-end (see §9). The user can ask for tests later.
- No gRPC, no Application Insights, no Polly v7 legacy API.

## 3. Repository layout

```
tutorial-net-resilience/
├── README.md                           (Italian)
├── TutorialNetResilience.slnx
├── Directory.Build.props               net10.0, Nullable, ImplicitUsings, LangVersion latest
├── Directory.Packages.props            central package management (all versions in one place)
├── .gitignore
├── docs/superpowers/specs/…            this document
└── src/
    ├── FlakyApi/                       ASP.NET Core minimal API, the unreliable dependency
    ├── Resilience.Shared/              class library: OTel bootstrap, Polly→span-event bridge, console narration
    ├── Example01.StandardHandler/
    ├── Example02.CustomPipeline/
    ├── Example03.Hedging/
    ├── Example04.DynamicReload/
    ├── Example05.NonHttpPipelines/
    └── Example06.Chaos/
```

Package versions (verified on nuget.org, 2026-09-21):

| Package | Version |
|---|---|
| Microsoft.Extensions.Hosting | 10.0.12 |
| Microsoft.Extensions.Resilience | 10.10.0 |
| Microsoft.Extensions.Http.Resilience | 10.10.0 |
| Polly.Core / Polly.Extensions / Polly.RateLimiting | 8.8.0 (transitive; pinned explicitly only where used directly) |
| OpenTelemetry.Extensions.Hosting | 1.19.1 |
| OpenTelemetry.Exporter.OpenTelemetryProtocol | 1.19.1 |
| OpenTelemetry.Instrumentation.Http | 1.19.0 |
| OpenTelemetry.Instrumentation.AspNetCore | 1.19.0 |
| OpenTelemetry.Instrumentation.Runtime | 1.19.0 |

## 4. Shared components

### 4.1 `Resilience.Shared`

| Type | Responsibility |
|---|---|
| `OpenTelemetryExtensions.AddDemoOpenTelemetry(this IHostApplicationBuilder, string serviceName)` | Registers the OTel SDK: resource (`service.name`, `service.version`, `service.instance.id`), **tracing** (`ActivitySource` `Resilience.Demo`, HttpClient instrumentation, AspNetCore instrumentation when the assembly is present), **metrics** (`Polly` meter, `System.Net.Http`, `Microsoft.AspNetCore.Hosting`, runtime instrumentation), **logs** (`builder.Logging.AddOpenTelemetry`, scopes + formatted message). OTLP exporter for all three signals, configured purely from the standard env vars (`OTEL_EXPORTER_OTLP_ENDPOINT`, `OTEL_EXPORTER_OTLP_PROTOCOL`, `OTEL_EXPORTER_OTLP_HEADERS`). Metric export interval shortened to 5 s so short-lived demos flush their counters. |
| `ResilienceTelemetryExtensions.AddDemoResilienceTelemetry(this IServiceCollection)` | Calls `AddResilienceEnricher()` (Microsoft.Extensions.Resilience: adds `request.name`, `request.dependency.name`, `error.type` to Polly metrics using `RequestMetadata` + `IExceptionSummarizer`), `AddExceptionSummarizer(b => b.AddHttpProvider())`, and registers `PollySpanEventsListener` into `TelemetryOptions.TelemetryListeners`. |
| `PollySpanEventsListener : TelemetryListener` | Polly has no tracing of its own. This listener turns every Polly telemetry event (`OnRetry`, `OnCircuitOpened`, `OnHedging`, `OnTimeout`, `OnFallback`, chaos events, …) into an `ActivityEvent` on `Activity.Current`, with tags `polly.pipeline`, `polly.strategy`, `polly.event`, `polly.attempt`, `polly.severity`, `polly.outcome`. Result: the scenario span in any trace viewer shows *when* each retry/hedge/break happened, next to the per-attempt HTTP client spans. |
| `DemoNarrator` | Console helper: `Step(string title, string why)` opens a numbered section and starts an `Activity` (`Resilience.Demo`) with tags `demo.example`, `demo.step`; `Info/Ok/Fail(string)` print colored lines; `Elapsed(Stopwatch)`. Each step is `IDisposable` to end the activity. |
| `FlakyApiClient` | Typed `HttpClient` wrapper used by the examples: `OkAsync()`, `FailThenOkAsync(int failures, string key)`, `UnstableAsync(double failRate)`, `SlowAsync(int ms)`, `StatusAsync(int code, int? retryAfter)`, `CreateOrderAsync()`. Returns a small `ApiResult` record (status, region, elapsed, body) and never throws for non-2xx (so scenarios can print the outcome); resilience exceptions (`BrokenCircuitException`, `TimeoutRejectedException`, `RateLimiterRejectedException`, `HttpRequestException`) propagate and are caught by the scenario. |
| `FlakyApiEndpoints` | Constants: `Primary = http://localhost:5100`, `SlowRegion = http://localhost:5101`. |

### 4.2 `FlakyApi`

Minimal API, single process, listens on **two URLs**: `http://localhost:5100` (region `eu-west`,
no extra latency) and `http://localhost:5101` (region `eu-east`, +2500 ms on every request).
Region behavior is bound from `appsettings.json` (`Regions:{port}:{Name,ExtraLatencyMs}`) and applied
by one middleware keyed on `HttpContext.Connection.LocalPort`. This is what makes hedging visible
without running two processes.

| Endpoint | Behavior | Used by |
|---|---|---|
| `GET /ok` | 200, `{ message, region, at }` | all |
| `GET /fail-then-ok?failures=N&key=K` | first N calls for key K → 500, then 200. In-memory `ConcurrentDictionary<string,int>`. Deterministic retry demo. | 01, 04 |
| `GET /unstable?failRate=0.5` | `Random.Shared.NextDouble() < failRate` → 500 else 200 | 02, 06 |
| `GET /slow?ms=3000` | `Task.Delay(ms, RequestAborted)` then 200. Honors cancellation so a client timeout actually stops server work (teaching point). | 02, 03 |
| `GET /status/{code}?retryAfter=S` | returns `code`; when `retryAfter` given adds `Retry-After: S` header (shows `ShouldRetryAfterHeader` in the standard handler) | 01 |
| `POST /orders` | always 500 (shows `DisableForUnsafeHttpMethods`: POST is *not* retried) | 01 |

Every response carries `X-Region` and `X-Handled-By-Port` headers. Instrumented with
`AddDemoOpenTelemetry("FlakyApi")` + AspNetCore instrumentation, so each client attempt is matched
by a server span in the same trace (W3C `traceparent` propagated by `HttpClient` automatically).

## 5. Examples

Every example is `Host.CreateApplicationBuilder(args)` + `AddDemoOpenTelemetry("ExampleNN")` +
`AddDemoResilienceTelemetry()` + a typed `FlakyApiClient` + one `Scenario` class with a
`RunAsync(CancellationToken)` executed from a `BackgroundService`-free top-level flow: build host,
`StartAsync`, run scenario, `StopAsync` (which flushes OTel). Each example's `Program.cs` is short;
the narrative and the resilience configuration live in `Scenario.cs` and `ResilienceConfig.cs`
respectively, so the reader sees the configuration in one screen.

### Example01.StandardHandler — the recommended default

*Feature:* `AddStandardResilienceHandler()` and its five stacked strategies (rate limiter → total
timeout → retry → circuit breaker → attempt timeout) with the shipped defaults.

Steps:
1. `/fail-then-ok?failures=2` with defaults → succeeds on 3rd attempt; narrator explains the
   exponential backoff + jitter and the 30 s total timeout budget.
2. Same client bound from configuration: `AddStandardResilienceHandler(config.GetSection("Resilience:Standard"))`
   with `Retry.MaxRetryAttempts = 5`, `Retry.Delay = 300ms`, `AttemptTimeout = 2s`. Shows the
   *why* of config binding (ops can tune without redeploy).
3. `/status/429?retryAfter=2` → shows the retry honors `Retry-After` (server-driven backoff).
4. `POST /orders` with `options.Retry.DisableForUnsafeHttpMethods()` → single attempt, 500
   surfaces immediately; narrator explains idempotency risk.
5. `ConfigureHttpClientDefaults(b => b.AddStandardResilienceHandler())` applied to every client, then
   a named client `"raw"` that calls `RemoveAllResilienceHandlers()` → `/fail-then-ok` fails on
   first attempt. Shows the opt-out pattern.

### Example02.CustomPipeline — composing your own

*Feature:* `AddResilienceHandler("custom", builder => …)` composing, outer→inner:
Fallback → Retry → CircuitBreaker → Timeout, with `HttpRetryStrategyOptions` /
`HttpCircuitBreakerStrategyOptions` / `HttpTimeoutStrategyOptions`, custom `ShouldHandle`
(`PredicateBuilder`), `OnRetry` / `OnOpened` / `OnClosed` / `OnHalfOpened` callbacks,
`CircuitBreakerStateProvider` (read state) and `CircuitBreakerManualControl` (isolate/close).

Steps:
1. `/slow?ms=5000` with 1 s attempt timeout → `TimeoutRejectedException` → retried twice → fallback
   returns a synthetic 200 "cached" response. Narrator: order matters, fallback is outermost.
2. `/unstable?failRate=1.0` × 10 with `MinimumThroughput=5`, `FailureRatio=0.5`,
   `BreakDuration=5s`, `SamplingDuration=10s` → circuit opens; subsequent calls fail fast with
   `BrokenCircuitException` without touching the network (narrator shows elapsed ≈ 0 ms and prints
   `StateProvider.CircuitState`).
3. Wait `BreakDuration` → half-open → `/ok` succeeds → closed.
4. `ManualControl.IsolateAsync()` → calls rejected; `CloseAsync()` → restored. Why: kill-switch for
   planned maintenance.

### Example03.Hedging — parallel attempts across endpoints

*Feature:* `AddStandardHedgingHandler()` with `ConfigureOrderedGroups` (primary = slow region
5101, secondary = fast region 5100), `SelectPipelineByAuthority()`, hedging `Delay`,
`MaxHedgedAttempts`. The routing replaces only scheme/host/port of the request URI, so the same
`/ok` path is sent to both regions.

Steps:
1. Without hedging: `/slow?ms=1500` on 5101 → ~4 s total (1500 + 2500 region latency).
2. With hedging, `Delay = 1s`: primary attempt on 5101 starts; after 1 s a hedged attempt goes to
   5100 and wins → ~2.5 s. Narrator prints which `X-Region` answered and the span events
   (`OnHedging`) on the trace.
3. Same with `ConfigureWeightedGroups` (50/50) to show A/B-style routing; narrator explains when
   to prefer weighted vs ordered and the cost (extra load) of hedging.

### Example04.DynamicReload — change behavior without restart

*Feature:* named options `services.Configure<HttpRetryStrategyOptions>("Retry", config.GetSection("Resilience:Retry"))`
+ `AddResilienceHandler("reloadable", (builder, context) => { context.EnableReloads<HttpRetryStrategyOptions>("Retry"); builder.AddRetry(context.GetOptions<HttpRetryStrategyOptions>("Retry")); })`.
`appsettings.json` is loaded with `reloadOnChange: true` by `CreateApplicationBuilder`, so a
file change → `IOptionsMonitor` change token → Polly rebuilds the pipeline. Also mentions that
`AddStandardResilienceHandler(section)` enables reloads automatically.

Steps:
1. `appsettings.json` has `MaxRetryAttempts = 1` → `/fail-then-ok?failures=3` fails.
2. The demo **rewrites the JSON file on disk** (`AppContext.BaseDirectory/appsettings.json`,
   `MaxRetryAttempts = 4`), waits for the change token (~1 s), repeats the call → succeeds.
   Narrator shows Polly's `OnReload` behavior and the pipeline rebuild in the logs.
3. Restores the file so the demo is repeatable.

### Example05.NonHttpPipelines — resilience is not only HTTP

*Feature:* `AddResiliencePipeline("orders-db", …)` and generic
`AddResiliencePipeline<string, Order?>("orders-db-read", …)` registered in DI, resolved through
`ResiliencePipelineProvider<string>`; `ResilienceContextPool` + `OperationKey`;
`context.SetRequestMetadata(new RequestMetadata { RequestName, DependencyName })` so the
Microsoft enricher tags the metrics; `AddRateLimiter` (`ConcurrencyLimiterOptions` via
`Polly.RateLimiting`) and `AddConcurrencyLimiter`; result-based `ShouldHandle` (retry when the
result is `null`).

The "database" is an in-process `FakeOrdersRepository` that fails the first N calls, sometimes
returns `null`, and sleeps to simulate slow queries.

Steps:
1. Non-generic pipeline: Retry + Timeout around `repo.SaveAsync` that throws twice.
2. Generic pipeline: `ResiliencePipeline<Order?>` retrying on `null` result.
3. Concurrency limiter `PermitLimit = 2, QueueLimit = 0`: 6 parallel calls → 2 run, 4 rejected with
   `RateLimiterRejectedException`; narrator explains bulkhead isolation and the `Polly` metric
   `resilience.polly.strategy.events{event.name="OnRateLimiterRejected"}`.

### Example06.Chaos — prove the pipeline works before production does

*Feature:* Polly's built-in chaos strategies (`Polly.Simmy`, shipped in Polly.Core):
`AddChaosLatency`, `AddChaosFault`, `AddChaosOutcome`, placed **last** in the pipeline
(innermost) so the "real" strategies above them (Retry, CircuitBreaker, Timeout) have to absorb
the injected faults. `InjectionRate` and `Enabled` bound from `appsettings.json`
(`Chaos:{Enabled,LatencyInjectionRate,FaultInjectionRate,OutcomeInjectionRate}`) through
`EnabledGenerator` / `InjectionRateGenerator` reading `IOptionsMonitor<ChaosOptions>`.

Steps:
1. Chaos disabled: 10 × `/ok` → all fast.
2. Chaos enabled (latency 2 s @ 30 %, `HttpRequestException` @ 20 %, synthetic 500 @ 20 %):
   10 × `/ok` → narrator tabulates attempts, retries, timeouts, and the fact that the *caller* still
   got 10 successful responses. Chaos events appear as span events and in
   `resilience.polly.strategy.events{event.name="OnLatencyInjected"|"OnFaultInjected"|"OnOutcomeInjected"}`.
3. Narrator: chaos is opt-in per environment; show the config that keeps it off in Production.

## 6. Data flow (one attempt)

```
Scenario step  (Activity "Resilience.Demo" — root span, tags demo.example/demo.step)
  └─ FlakyApiClient.XxxAsync()
       └─ HttpClient  → resilience DelegatingHandler (Polly pipeline)
            ├─ attempt 1: HttpClient span (OpenTelemetry.Instrumentation.Http) → FlakyApi server span
            │    └─ Polly event OnRetry → PollySpanEventsListener → ActivityEvent on the step span
            │       + counter resilience.polly.strategy.events + log "Polly" category
            └─ attempt 2: …
```

Metrics the reader should look for in their backend (documented in README):
`resilience.polly.strategy.events`, `resilience.polly.strategy.attempt.duration`,
`resilience.polly.pipeline.duration` (tags: `pipeline.name`, `strategy.name`, `event.name`,
`attempt.number`, `request.dependency.name`, `error.type`), plus `http.client.request.duration`
and `http.server.request.duration`.

## 7. Configuration & running

- Every process reads `OTEL_EXPORTER_OTLP_ENDPOINT` (default `http://localhost:4317`, gRPC) and
  `OTEL_EXPORTER_OTLP_PROTOCOL`; nothing else is required.
- `FlakyApi` must be started first (`dotnet run --project src/FlakyApi`). Each example checks
  `/ok` on startup and prints a clear message if the API is not reachable, then exits with code 2.
- README gives the exact commands (PowerShell) for FlakyApi and each example, and a table
  "what to look at in your OTel backend" per example.
- `launchSettings.json` is not used for examples (plain console); FlakyApi's `appsettings.json`
  sets `Kestrel:Endpoints` for the two ports.

## 8. Error handling

- Scenarios catch the specific resilience exceptions (`BrokenCircuitException`,
  `TimeoutRejectedException`, `RateLimiterRejectedException`, `HttpRequestException`,
  `OperationCanceledException`) and print them as *expected outcomes* with the reason; any other
  exception is unexpected and fails the run (exit code 1).
- `FlakyApiClient` never throws on non-2xx; the status code is part of the result so the
  narrator can print "500 on attempt 1, 200 on attempt 3".
- OTLP export failures (no collector running) are logged by the SDK at warning level once; the demo
  still runs. README says so explicitly.
- Ctrl+C cancels the scenario through the host's `ApplicationStopping` token and still flushes
  telemetry.

## 9. Verification (no test project)

1. `dotnet build TutorialNetResilience.slnx` — zero warnings as errors (`TreatWarningsAsErrors`
   on).
2. Run `FlakyApi`, then each example; compare console output to the expected narrative per step
   (attempt counts, circuit state transitions, which region answered, rate-limiter rejections).
3. OTLP smoke check during development only: point `OTEL_EXPORTER_OTLP_ENDPOINT` at a throwaway
   local OTLP/HTTP receiver (a 30-line Kestrel app in the scratchpad, not committed) and confirm
   traces, metrics and logs arrive for at least one example. This validates the exporter wiring
   without imposing a backend on the reader.

## 10. README outline (Italian)

1. Cos'è la resilienza e perché `Microsoft.Extensions.Resilience` (Polly v8 + integrazione DI /
   telemetria / HttpClientFactory).
2. Prerequisiti e avvio (SDK 10, variabili OTLP, avvio di FlakyApi).
3. Struttura del repository.
4. Per ogni esempio: obiettivo, cosa viene fatto, perché, cosa osservare (console + tracce +
   metriche + log).
5. Telemetria: come è cablata (OTel SDK, `Polly` meter, enricher Microsoft, span events),
   tabella delle metriche e dei tag.
6. Riferimenti (docs Microsoft, pollydocs).
