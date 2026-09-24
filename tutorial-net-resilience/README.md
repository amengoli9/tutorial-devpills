# tutorial-net-resilience

Tutorial per DevPills su **Microsoft.Extensions.Resilience** e **Microsoft.Extensions.Http.Resilience**
(Polly v8 integrato in .NET), con tutto strumentato tramite **OpenTelemetry** ed esportato via **OTLP**.

Sei esempi, uno per progetto, ognuno racconta uno scenario passo-passo in console: cosa sta per fare,
perché, e cosa è successo. Le chiamate vanno verso `FlakyApi`, un'API volutamente inaffidabile che
fa parte della soluzione.

## Cos'è la resilienza e perché questi pacchetti

Ogni chiamata a una dipendenza remota può fallire in modo *transitorio*: un timeout, un 503 durante
un deploy, una connessione resettata. La resilienza è l'insieme di strategie che permettono a un
sistema di assorbire questi errori senza propagarli all'utente: **retry**, **timeout**,
**circuit breaker**, **hedging**, **rate limiting**, **fallback**.

[Polly](https://github.com/App-vNext/Polly) è la libreria .NET di riferimento. Dalla v8 Microsoft la
integra nella piattaforma con due pacchetti:

| Pacchetto | Cosa aggiunge |
|---|---|
| `Microsoft.Extensions.Resilience` | Registrazione delle pipeline in DI (`AddResiliencePipeline`), `ResiliencePipelineProvider`, arricchimento della telemetria Polly (`AddResilienceEnricher`) con i metadati della richiesta e un riassunto dell'eccezione. |
| `Microsoft.Extensions.Http.Resilience` | Integrazione con `IHttpClientFactory`: `AddStandardResilienceHandler`, `AddStandardHedgingHandler`, `AddResilienceHandler`, opzioni HTTP-aware (`HttpRetryStrategyOptions`, …) già configurate per gestire 5xx/408/429, `HttpRequestException` e `TimeoutRejectedException`. |

Versioni usate (settembre 2026, .NET 10):

| Pacchetto | Versione |
|---|---|
| Microsoft.Extensions.Hosting | 10.0.12 |
| Microsoft.Extensions.Resilience / Http.Resilience | 10.10.0 |
| Polly.Core / Polly.Extensions / Polly.RateLimiting (transitivi) | 8.4.2 |
| OpenTelemetry.Extensions.Hosting / Exporter.OpenTelemetryProtocol | 1.19.1 |
| OpenTelemetry.Instrumentation.Http / AspNetCore / Runtime | 1.19.0 |

Le versioni sono centralizzate in `Directory.Packages.props` (Central Package Management).

## Prerequisiti

- .NET SDK 10.
- Un ricevitore OTLP a scelta (OpenTelemetry Collector, Jaeger, Grafana Tempo/Loki/Mimir, Seq, …).
  Gli esempi usano **solo** l'exporter OTLP e leggono le variabili d'ambiente standard:

  ```powershell
  $env:OTEL_EXPORTER_OTLP_ENDPOINT = "http://localhost:4317"   # default: gRPC su 4317
  $env:OTEL_EXPORTER_OTLP_PROTOCOL = "grpc"                     # oppure "http/protobuf" (tipicamente porta 4318)
  ```

  Senza ricevitore gli esempi funzionano comunque: l'export fallisce in silenzio (l'SDK segnala
  gli errori di export solo tramite la sua
  [self-diagnostics](https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/src/OpenTelemetry/README.md#self-diagnostics))
  e la demo prosegue.

## Struttura del repository

```
tutorial-net-resilience/
├── TutorialNetResilience.slnx
├── Directory.Build.props            net10.0, Nullable, TreatWarningsAsErrors
├── Directory.Packages.props         versioni dei pacchetti
├── nuget.config                     solo nuget.org, restore riproducibile
└── src/
    ├── FlakyApi/                    API inaffidabile: due porte = due "regioni"
    ├── Resilience.Shared/           bootstrap OpenTelemetry, listener Polly, narrazione console
    ├── Example01.StandardHandler/   AddStandardResilienceHandler
    ├── Example02.CustomPipeline/    AddResilienceHandler: fallback + retry + circuit breaker + timeout
    ├── Example03.Hedging/           AddStandardHedgingHandler con routing ordered/weighted
    ├── Example04.DynamicReload/     EnableReloads: cambiare le opzioni a runtime
    ├── Example05.NonHttpPipelines/  AddResiliencePipeline per dipendenze non HTTP, bulkhead, throttling
    └── Example06.Chaos/             chaos engineering integrato in Polly
```

Ogni esempio è composto da tre file: `Program.cs` (bootstrap, poche righe), `ResilienceConfig.cs`
(la feature, in una schermata) e `Scenario.cs` (i passi narrati con il *perché*).

## Come eseguire

1. In un terminale, avvia l'API inaffidabile (resta in esecuzione):

   ```powershell
   dotnet run --project src/FlakyApi
   ```

2. In un secondo terminale, imposta le variabili OTLP (vedi sopra) e lancia un esempio:

   ```powershell
   dotnet run --project src/Example01.StandardHandler
   ```

   Ogni esempio controlla che `FlakyApi` risponda, esegue lo scenario, svuota la telemetria ed esce
   (exit code 0; 2 se `FlakyApi` non è raggiungibile).

### FlakyApi

Un unico processo che ascolta su due porte. La porta decide la "regione" e la latenza aggiuntiva,
così l'esempio di hedging può mettere in gara una regione lenta contro una veloce senza avviare due
processi.

| Porta | Regione | Latenza extra |
|---|---|---|
| 5100 | `eu-west` | 0 ms |
| 5101 | `eu-east` | +2500 ms su ogni richiesta |

| Endpoint | Comportamento | Usato da |
|---|---|---|
| `GET /ok` | 200 | tutti |
| `GET /fail-then-ok?failures=N&key=K` | le prime N chiamate per la chiave K → 500, poi 200 (fallimento transitorio deterministico) | 01, 04 |
| `GET /unstable?failRate=0.5` | 500 con probabilità `failRate` | 02 |
| `GET /slow?ms=3000` | attende `ms` e risponde 200; rispetta la cancellazione (se il client fa timeout, il server smette di lavorare) | 01, 02, 03 |
| `GET /status/{code}?retryAfter=S` | risponde `code`; con `retryAfter` aggiunge l'header `Retry-After` | 01 |
| `POST /orders` | sempre 500 (mostra che i metodi non idempotenti non vanno ritentati) | 01 |

Ogni risposta porta gli header `X-Region` e `X-Handled-By-Port`.

## Gli esempi

### Example01 — `AddStandardResilienceHandler()`: il default consigliato

**Cosa fa.** Una riga aggiunge a un `HttpClient` cinque strategie impilate con default pronti per
la produzione:

| Ordine | Strategia | Default |
|---|---|---|
| 1 | Rate limiter | 1000 richieste concorrenti, coda 0 |
| 2 | Total timeout | 30 s per l'intera esecuzione, retry inclusi |
| 3 | Retry | 3 tentativi, backoff esponenziale con jitter, base 2 s |
| 4 | Circuit breaker | apre con 10 % di errori su 30 s (minimo 100 chiamate), resta aperto 5 s |
| 5 | Attempt timeout | 10 s per singolo tentativo |

Gestisce HTTP 5xx, 408, 429, `HttpRequestException` e `TimeoutRejectedException`.

```csharp
// Politica globale: ogni HttpClient creato da IHttpClientFactory riceve lo standard handler.
services.ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler());

// Stesse 5 strategie, ma i numeri arrivano da appsettings.json (sezione "Resilience:Standard").
services.AddHttpClient(FromConfig, ...)
    .RemoveAllResilienceHandlers()
    .AddStandardResilienceHandler(configuration.GetSection("Resilience:Standard"));

// Mai ritentare POST/PUT/PATCH/DELETE: un POST ripetuto può creare ordini duplicati.
services.AddHttpClient(NoUnsafeRetries, ...)
    .RemoveAllResilienceHandlers()
    .AddStandardResilienceHandler(options => options.Retry.DisableForUnsafeHttpMethods());

// Opt-out completo.
services.AddHttpClient(Raw, ...).RemoveAllResilienceHandlers();
```

**Perché.** Il 90 % delle applicazioni non ha bisogno di una pipeline su misura: ha bisogno di
*un* default sensato applicato ovunque, con i numeri in configurazione (così ops può ritoccarli senza
un deploy) e la possibilità di escludere i client che devono fallire subito.

> `RemoveAllResilienceHandlers()` è ancora marcato `[Experimental]` (diagnostica `EXTEXP0001`)
> nella 10.10: l'esempio fa opt-in esplicito con `#pragma warning disable EXTEXP0001`.

**Cosa osservare.**
- Console: `/fail-then-ok?failures=2` risponde 500, 500, 200 ma il chiamante vede solo il 200 finale;
  le righe `⚙ polly` mostrano ogni tentativo e l'attesa di backoff.
- Con `/status/429?retryAfter=1` i retry sono distanziati esattamente 1 s: la strategia rispetta
  l'header `Retry-After` (`HttpRetryStrategyOptions.ShouldRetryAfterHeader`).
- `POST /orders` fa un solo tentativo; il client "raw" nessun retry.
- Tracce: sotto lo span dello step ci sono N span HTTP (uno per tentativo) e gli eventi `polly.OnRetry`.
- Metriche: `resilience.polly.strategy.events{event.name="OnRetry"}` sale a ogni retry.

### Example02 — `AddResilienceHandler()`: comporre la pipeline a mano

**Cosa fa.** Costruisce una pipeline con quattro strategie. L'ordine conta: si aggiungono dalla più
esterna alla più interna e la richiesta le attraversa in quell'ordine.

```
fallback → retry → circuit breaker → attempt timeout → HttpClient
```

```csharp
.AddResilienceHandler("custom", builder =>
{
    builder.AddFallback(new FallbackStrategyOptions<HttpResponseMessage> { ShouldHandle = ...Handle<TimeoutRejectedException>(), FallbackAction = ... });
    builder.AddRetry(new HttpRetryStrategyOptions { MaxRetryAttempts = 2, Delay = 200ms, OnRetry = ... });
    builder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
    {
        SamplingDuration = 10s, MinimumThroughput = 5, FailureRatio = 0.5, BreakDuration = 5s,
        StateProvider = CircuitState,      // leggere lo stato (dashboard, health check)
        ManualControl = CircuitControl,    // aprire/chiudere a mano (kill switch)
        OnOpened = ..., OnHalfOpened = ..., OnClosed = ...
    });
    builder.AddTimeout(new HttpTimeoutStrategyOptions { Timeout = 1s });
});
```

**Perché.** Un timeout per tentativo evita di restare appesi; il retry assorbe i fallimenti
isolati; il circuit breaker smette di martellare una dipendenza già in difficoltà (e fa fallire le
chiamate in ~0 ms senza toccare la rete); il fallback restituisce una risposta degradata invece di
un'eccezione. Le callback `OnRetry`/`OnOpened`/… sono il punto giusto per log e alert applicativi.

**Cosa osservare.**
- Step 1: `/slow?ms=5000` → 3 timeout da 1 s, 2 retry, poi il fallback risponde 200 da `fallback-cache`.
- Step 2: il breaker si apre (`OnCircuitOpened`) e le chiamate successive falliscono con
  `BrokenCircuitException` in 0 ms: nessuno span HTTP nella traccia.
- Step 3: dopo 5 s `OnCircuitHalfOpened`, la chiamata di prova riesce, `OnCircuitClosed`.
- Step 4: `IsolateAsync()` → `IsolatedCircuitException`; `CloseAsync()` → torna tutto normale.
- Metriche: `resilience.polly.strategy.events{event.name="OnCircuitOpened"|"OnFallback"|"OnTimeout"}`.

### Example03 — `AddStandardHedgingHandler()`: tentativi paralleli

**Cosa fa.** L'hedging non aspetta un tentativo lento: dopo `Hedging.Delay` ne lancia un altro in
parallelo e prende la prima risposta, cancellando l'altra. Il routing decide dove va ogni tentativo;
degli URI configurati vengono usati solo schema, host e porta, path e query restano quelli della
richiesta.

```csharp
.AddStandardHedgingHandler(routing => routing.ConfigureOrderedGroups(options =>
{
    options.Groups.Add(new UriEndpointGroup { Endpoints = { new() { Uri = SlowRegion, Weight = 1 } } });
    options.Groups.Add(new UriEndpointGroup { Endpoints = { new() { Uri = FastRegion, Weight = 1 } } });
}))
.Configure(options => { options.Hedging.Delay = TimeSpan.FromSeconds(1); options.Hedging.MaxHedgedAttempts = 2; })
.SelectPipelineByAuthority();   // un circuit breaker / rate limiter / timeout per ogni schema+host+porta
```

`ConfigureWeightedGroups` invece sceglie il gruppo del *primo* tentativo in base al peso
(A/B test, canary, traffic split); gli hedge successivi vanno ai gruppi rimanenti.

**Perché.** Riduce la latenza di coda (p99) quando una regione o un'istanza è lenta, al prezzo di
carico extra sulla dipendenza. Va usato solo su richieste idempotenti.

**Cosa osservare.**
- Baseline senza hedging: `/slow?ms=1500` su `eu-east` → ~4 s.
- Con hedging ordinato: ~2,5 s, risposta da `eu-west`; in console `attempt #1 → TaskCanceledException`
  è il tentativo perdente cancellato.
- Tracce: due span HTTP fratelli sotto lo step; quello verso 5101 termina con status 499 lato server
  (richiesta abortita) e l'evento `polly.OnHedging` segna il momento dell'hedge.
- Con i gruppi pesati alcune chiamate partono da `eu-east` e vengono "salvate" dopo 1 s, altre
  partono da `eu-west` e rispondono subito.

### Example04 — `EnableReloads()`: cambiare le opzioni senza riavviare

**Cosa fa.** Lega `HttpRetryStrategyOptions` a una sezione di `appsettings.json` (caricato con
`reloadOnChange`) e abilita il rebuild della pipeline quando le opzioni cambiano.

```csharp
services.Configure<HttpRetryStrategyOptions>("Retry", configuration.GetSection("Resilience:Retry"));

.AddResilienceHandler("reloadable-pipeline", (builder, context) =>
{
    context.EnableReloads<HttpRetryStrategyOptions>("Retry");            // 1. ricostruisci al cambio
    var configured = context.GetOptions<HttpRetryStrategyOptions>("Retry"); // 2. leggi i valori correnti
    builder.AddRetry(new HttpRetryStrategyOptions { MaxRetryAttempts = configured.MaxRetryAttempts, ... });
});
```

Anche `AddStandardResilienceHandler(section)` abilita i reload automaticamente.

**Perché.** Le impostazioni di resilienza si ritoccano *durante* un incidente, cioè proprio quando
non si può fare un deploy.

**Cosa osservare.** La demo riscrive da sola l'`appsettings.json` nella cartella di output
(`MaxRetryAttempts` 1 → 4), aspetta la notifica di `IOptionsMonitor`, ripete la chiamata e infine
ripristina il file. In console: `OnReload` e `pipeline built with MaxRetryAttempts = 4`; la stessa
chiamata prima fallisce e poi riesce al 4º tentativo. Su Windows il file watcher può notificare due
volte: è normale.

### Example05 — `AddResiliencePipeline()`: non solo HTTP

**Cosa fa.** Registra pipeline con chiave in DI e le usa intorno a un "database" finto.

```csharp
services.AddResiliencePipeline("orders-db-save", builder => builder.AddRetry(...).AddTimeout(2s));      // non generica
services.AddResiliencePipeline<string, Order?>("orders-db-read", builder =>                              // generica: decide il RISULTATO
    builder.AddRetry(new RetryStrategyOptions<Order?> { ShouldHandle = new PredicateBuilder<Order?>().HandleResult(o => o is null) }));
services.AddResiliencePipeline("orders-db-bulkhead", builder => builder.AddConcurrencyLimiter(permitLimit: 2, queueLimit: 0));
services.AddResiliencePipeline("orders-db-throttle", builder => builder.AddRateLimiter(new SlidingWindowRateLimiter(...)));

// uso
var pipeline = provider.GetPipeline("orders-db-save");            // ResiliencePipelineProvider<string>
var context = ResilienceContextPool.Shared.Get("SaveOrder", ct);   // operation key → tag operation.key
context.SetRequestMetadata(new RequestMetadata { RequestName = "SaveOrder", DependencyName = "OrdersDb" });
await pipeline.ExecuteAsync(static async (ctx, state) => await state.Repository.SaveAsync(state.Order, ctx.CancellationToken), context, state);
```

**Perché.** Le stesse strategie valgono per database, code, cache, gRPC. Il **bulkhead**
(concurrency limiter) protegge la dipendenza e il processo da un burst; il **throttling** ti tiene
dentro una quota prima che il provider risponda 429. `RequestMetadata` + `AddResilienceEnricher()`
aggiungono alle metriche i tag `request.name` e `request.dependency.name`.

**Cosa osservare.**
- Retry su eccezione (`TimeoutException`) e retry su *risultato* (`null` = replica in ritardo).
- Bulkhead: 6 chiamate parallele, 2 eseguite, 4 rifiutate subito con `RateLimiterRejectedException`.
- Throttle: 3 chiamate al secondo, la 4ª e la 5ª rifiutate.
- Metriche: `resilience.polly.strategy.events{event.name="OnRateLimiterRejected"}` e i tag
  `operation.key="SaveOrder"`, `request.dependency.name="OrdersDb"`.

### Example06 — Chaos engineering integrato in Polly

**Cosa fa.** Polly v8 include le strategie di chaos (ex Simmy): `AddChaosLatency`, `AddChaosFault`,
`AddChaosOutcome` (e `AddChaosBehavior`). Vanno messe **per ultime** (le più interne), così retry,
circuit breaker e timeout vedono i guasti iniettati esattamente come vedrebbero quelli reali.

```csharp
builder.AddRetry(...); builder.AddCircuitBreaker(...); builder.AddTimeout(1s);   // le strategie sotto test
builder.AddChaosLatency(new ChaosLatencyStrategyOptions
{
    EnabledGenerator = _ => ValueTask.FromResult(chaos.CurrentValue.Enabled),               // letto a ogni esecuzione
    InjectionRateGenerator = _ => ValueTask.FromResult(chaos.CurrentValue.LatencyInjectionRate),
    LatencyGenerator = _ => ValueTask.FromResult(TimeSpan.FromMilliseconds(chaos.CurrentValue.LatencyMs)),
});
builder.AddChaosFault(new ChaosFaultStrategyOptions { FaultGenerator = new FaultGenerator().AddException(() => new HttpRequestException("...")) , ... });
builder.AddChaosOutcome(new ChaosOutcomeStrategyOptions<HttpResponseMessage> { OutcomeGenerator = new OutcomeGenerator<HttpResponseMessage>().AddResult(() => new HttpResponseMessage(HttpStatusCode.InternalServerError)), ... });
```

I parametri (`Enabled`, tassi di iniezione, latenza) arrivano da `appsettings.json` tramite
`IOptionsMonitor<ChaosOptions>`. Una post-configurazione forza `Enabled=false` in Production,
anche quando un file o un override richiede `true`.

`dotnet run --project src/Example06.Chaos` usa il profilo `Demo` in Development per mostrare
la sequenza spento → acceso → spento. Con `--no-launch-profile` l'ambiente predefinito è
Production (salvo `DOTNET_ENVIRONMENT`); in alternativa si può passare `-- --environment Production`.
Gli override di `Chaos:Enabled` da variabili d'ambiente, riga di comando o file specifici
dell'ambiente vengono rispettati: la demo esegue un batch con il valore effettivo e salta le
modifiche automatiche del file, senza aspettare un reload impossibile.

**Perché.** Una pipeline di resilienza che non è mai stata messa alla prova è una speranza, non una
garanzia. Il chaos permette di verificarla in Development o staging senza
aspettare l'incidente vero.

**Cosa osservare.** Con il chaos spento 10/10 chiamate in pochi ms; con il chaos acceso le righe
`Chaos.OnLatency` / `Chaos.OnFault` / `Chaos.OnOutcome` mostrano cosa è stato iniettato e retry +
timeout lo assorbono: il chiamante vede (quasi) tutte risposte 200. Una chiamata fallisce solo se
4 tentativi consecutivi vengono tutti colpiti. Metriche:
`resilience.polly.strategy.events{event.name="Chaos.OnLatency"|"Chaos.OnFault"|"Chaos.OnOutcome"}`.

## Telemetria

### Come è cablata

Tutto sta in `Resilience.Shared`:

- **`AddDemoOpenTelemetry(serviceName)`** registra l'SDK OpenTelemetry con:
  - *Traces*: `ActivitySource` `Resilience.Demo` (uno span per step della demo) +
    `AddHttpClientInstrumentation()` (uno span per tentativo HTTP) + `AddAspNetCoreInstrumentation()`
    in `FlakyApi` (span server). Il `traceparent` viaggia da solo: client e server finiscono nella
    stessa traccia.
  - *Metrics*: meter `Polly`, `System.Net.Http`, `Microsoft.AspNetCore.Hosting`, runtime.
    Intervallo di export ridotto a 5 s perché le demo durano pochi secondi.
  - *Logs*: `WithLogging()` con scope e messaggio formattato.
  - Un solo exporter, OTLP, per i tre segnali, configurato esclusivamente dalle variabili
    `OTEL_EXPORTER_OTLP_*`.
- **`AddDemoResilienceTelemetry()`**:
  - `AddResilienceEnricher()` (Microsoft.Extensions.Resilience): aggiunge alle metriche Polly
    `request.name`, `request.dependency.name` (da `RequestMetadata` nel `ResilienceContext`) ed
    `error.type` (da `IExceptionSummarizer`).
  - `PollySpanEventsListener`: Polly non produce span. Questo `TelemetryListener` trasforma ogni
    evento Polly (`OnRetry`, `OnCircuitOpened`, `OnHedging`, `OnTimeout`, `OnFallback`,
    `Chaos.*`, …) in un `ActivityEvent` sullo span corrente, con i tag `polly.pipeline`,
    `polly.strategy`, `polly.event`, `polly.attempt`, `polly.http.status` / `polly.exception`.
    Nel trace viewer vedi *quando* è scattato ogni retry, accanto agli span HTTP dei tentativi.
  - `PollyConsoleListener`: una riga compatta in console per ogni evento (`⚙ polly …`). I log
    `ILogger` di Polly, completi di stack trace, vanno solo all'exporter OTLP (categoria `Polly`,
    livello da `appsettings.json`).

### Metriche Polly

| Strumento | Tipo | Significato |
|---|---|---|
| `resilience.polly.strategy.events` | counter | un evento per ogni decisione di una strategia (retry, apertura breaker, hedge, timeout, rifiuto del rate limiter, chaos…) |
| `resilience.polly.strategy.attempt.duration` | histogram (ms) | durata di ogni tentativo eseguito da retry/hedging |
| `resilience.polly.pipeline.duration` | histogram (ms) | durata dell'intera esecuzione della pipeline |

Tag: `pipeline.name`, `pipeline.instance`, `strategy.name`, `event.name`, `event.severity`,
`operation.key`, `exception.type`, `attempt.number`, `attempt.handled`; con l'enricher Microsoft
anche `request.name`, `request.dependency.name`, `error.type`.

Metriche HTTP standard: `http.client.request.duration` (lato esempi) e
`http.server.request.duration` (lato `FlakyApi`).

### Come leggere una traccia

```
Example02.CustomPipeline / step 1: Timeout → retry → fallback     (span radice, tag demo.example, demo.step)
├── GET /slow                                                     (tentativo 1, span client HTTP)
│   └── GET /slow                                                 (span server di FlakyApi, status 499: abortita)
├── ● polly.OnTimeout   ● polly.OnRetry (polly.attempt=0)          (eventi sullo span radice)
├── GET /slow                                                     (tentativo 2)
├── ● polly.OnTimeout   ● polly.OnRetry (polly.attempt=1)
├── GET /slow                                                     (tentativo 3)
└── ● polly.OnTimeout   ● polly.OnFallback
```

## Verifica

Verifica delle regressioni (senza avviare FlakyApi o un ricevitore OTLP):

```powershell
dotnet test TutorialNetResilience.slnx
```

I test verificano la precedenza degli override e il blocco del chaos in Production, i dettagli
di telemetria dell'hedging e l'assenza di retry nel probe, incluso timeout e cancellazione.

## Riferimenti

- [Introduzione alla resilienza in .NET](https://learn.microsoft.com/dotnet/core/resilience/)
- [Build resilient HTTP apps (Microsoft.Extensions.Http.Resilience)](https://learn.microsoft.com/dotnet/core/resilience/http-resilience)
- [Polly — strategie](https://www.pollydocs.org/strategies/) · [Chaos](https://www.pollydocs.org/chaos/) · [Telemetria](https://www.pollydocs.org/advanced/telemetry)
- [OpenTelemetry .NET](https://opentelemetry.io/docs/languages/dotnet/)
