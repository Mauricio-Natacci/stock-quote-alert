# Architecture

## Layers

```text
                ┌────────────────────────────────── StockQuoteAlert.Cli ───────────────────────────────────┐
 arguments ───► │ CommandLineParser ──► MonitoringTargets (one or more stocks)                             │
                │ DependencyInjection (composition root)    Blazor dashboard + JSON API                    │◄── browser
                │ Program: exit code from MonitoringOutcome; ConfigurationErrors → exit code 2             │
                └──────────────────────────────────────────────────────────────────────────────────────────┘
                               │
                ┌────────────────────────────────── StockQuoteAlert.Core ──────────────────────────────────┐
                │ MonitoringWorker ──(PeriodicTimer)──► StockMonitor × N (one per stock)                   │
                │                                         ├ classifies the price (AlertThresholds)         │
                │                                         └ publishes PriceAlertRaised                     │
                │ Domain: Ticker, AlertThresholds, PriceZone, Quote, PriceAlert · BrazilianFormat          │
                │   ports:  IQuoteProvider   IAlertPublisher   IAlertConsumer   IMonitoringJournal         │
                └──────────────────────────────────────────────────────────────────────────────────────────┘
                               │
                ┌───────────────────────────── StockQuoteAlert.Infrastructure ─────────────────────────────┐
                │ FallbackQuoteProvider        InMemoryAlertQueue ──► AlertDispatcher (retry)              │
                │  └ QuoteSourceAdapter ×2                              ├ EmailAlertConsumer ──► SMTP      │
                │     ├ YahooFinanceQuoteSource                         │  (AlertEmailComposer + MailKit)  │
                │     └ BrapiQuoteSource                                ├ SlackAlertConsumer (example)     │
                │       (HttpClient + resilience)                       └ SmsAlertConsumer   (example)     │
                │ InMemoryMonitoringJournal                                                                │
                └──────────────────────────────────────────────────────────────────────────────────────────┘
```

Dependencies point inwards: `Cli → Infrastructure → Core`. The **Core** knows nothing about HTTP, SMTP or ASP.NET.
It defines the ports (`IQuoteProvider`, `IAlertPublisher`, `IAlertConsumer`, `IMonitoringJournal`), and the
infrastructure provides the implementations. The only wiring of concrete classes happens in the two
`DependencyInjection` classes (Cli and Infrastructure).

## Flow of a check

1. `MonitoringWorker` (a `BackgroundService`) calls `CheckAsync` on every `StockMonitor` in parallel, right away and
   then every `Monitoring:PollingInterval`, using a `PeriodicTimer` driven by `TimeProvider`. There is one
   `StockMonitor` per stock, each with its own `MonitoringTarget` and current zone.
2. `StockMonitor` fetches the quote through `IQuoteProvider`, classifies the price into a `PriceZone` and records the
   observation in the journal.
3. If the zone changed to "above sell" or "below buy", it creates a `PriceAlert` and publishes a `PriceAlertRaised`
   message through `IAlertPublisher`. Publishing only enqueues: the monitor never waits for a delivery.
4. In the background, `AlertDispatcher` delivers the message to every registered `IAlertConsumer` (see
   [Alert messaging](#alert-messaging)). `EmailAlertConsumer` builds the e-mail (`AlertEmailComposer`), sends it
   (`MailKitEmailSender`) and records a `NotificationRecord` in the journal with the content, recipient, time and
   status (`Sent`/`Failed`).
5. The journal stores the records and publishes an event to each subscriber. Every open dashboard receives the
   event, rebuilds only the stock it concerns and renders again; Blazor sends the difference to the browser.

When a check fails, the worker records a `MonitoringFailure` classified as transient, unknown ticker or rejected.
A transient failure is retried on the next tick; an unrecoverable one stops that stock. When no stock is left, the
worker marks `MonitoringOutcome` and stops the application, and the CLI exits with code 3.

## Alert messaging

Alert delivery follows the publisher/consumer model, like a message queue inside the process:

```text
StockMonitor ──publishes──► IAlertPublisher ──► InMemoryAlertQueue (Channel<PriceAlertRaised>)
                                                       │
                                                       ▼
                                       AlertDispatcher (BackgroundService)
                                   ┌───────────────────┼───────────────────┐
                                   ▼                   ▼                   ▼
                          EmailAlertConsumer   SlackAlertConsumer   SmsAlertConsumer
                          (sends the e-mail)   (example: log only)  (example: log only)
```

- **Publisher**: `IAlertPublisher` (a Core port). The `InMemoryAlertQueue` implementation writes the message to an
  unbounded `Channel` and returns immediately, so a slow SMTP server never delays the quote checks.
- **Message**: `PriceAlertRaised` carries an `Id` (UUID v7), the `PriceAlert` and the time it was raised. Every
  consumer receives the same instance.
- **Consumers**: each `IAlertConsumer` has a `Name` and decides what to do with the alert. Throwing means "I could
  not handle it"; the dispatcher takes care of the retry.
- **Dispatcher**: `AlertDispatcher` reads the queue in order and delivers each message to all consumers in parallel.
  Consumers are independent: if one fails, the others still receive the message exactly once. A failed delivery is
  retried after 5 s, 10 s and 20 s (at most 4 attempts); after that it is given up, with an error log. The next
  message waits until every consumer is done with the current one.
- **Examples**: `SlackAlertConsumer` and `SmsAlertConsumer` integrate with nothing; they only log that they received
  the alert. They show where a real integration would plug in.

To add a channel, implement `IAlertConsumer` and register it in dependency injection
(`services.AddSingleton<IAlertConsumer, MyConsumer>()`). Neither `StockMonitor` nor the other consumers change.

The queue lives in memory: messages not yet delivered are lost if the process stops. Replacing `InMemoryAlertQueue`
and `AlertDispatcher` with a real broker (RabbitMQ, Azure Service Bus) would change neither the Core nor the
consumers. This is on the [roadmap](ROADMAP.md#messaging).

## Decisions

### Core requirements

| Decision | Reason |
| --- | --- |
| **Alert on zone transition** | Implicit requirement: without it, a price sitting above the reference sends one e-mail per check. The zone is recorded as soon as the alert is published; making sure it is delivered is the dispatcher's job. References are exclusive: a price equal to one counts as neutral. |
| **Yahoo Finance as the quote API** | It needs no API key, so the project runs right after cloning. It is an unofficial API, which is why a backup provider exists (see below). |
| **Sources + a single adapter** | Each `IQuoteSource` only talks to its API and returns a `RawQuote`. `QuoteSourceAdapter` turns any source into the domain `Quote` with the same rules (price rounded to cents, time in UTC, positive price, source name). The output is identical whatever the provider, and a new provider only needs the HTTP call. |
| **One exception contract for every source** | `HttpQuoteRequest` turns every HTTP failure (network error, timeout, resilience rejection, malformed JSON, unexpected status) into a `QuoteProviderException`; 404 becomes `TickerNotFoundException`. Each source only adds what is specific to it (brapi: a rejected token is unrecoverable). |
| **MailKit** | Microsoft marks `System.Net.Mail.SmtpClient` as not recommended for new code. MailKit is the suggested alternative and exposes `ISmtpClient`, which is the boundary used by the tests. |
| **Options validated with `ValidateOnStart`** | Configuration is validated before any work starts: DataAnnotations for simple rules and `IValidatableObject` for rules spanning several fields (credentials required only when `RequireAuthentication` is on; no password over an unencrypted connection). `ConfigurationErrors` prints every problem without the framework wrapper and the CLI exits with code 2, also for values that cannot be read at all (e.g. `Smtp:Port=abc`). |
| **Recoverable × unrecoverable failures** | `QuoteProviderException.IsRecoverable`: network errors and HTTP 5xx keep retrying; an unknown ticker or a rejected token stops the stock instead of repeating the same error forever. The worker marks `MonitoringOutcome` when no stock is left and the CLI reads it to exit with code 3, so the Core never touches process-wide state (`Environment.ExitCode`). |
| **`Microsoft.Extensions.Http.Resilience`** | Standard handler with retry and exponential backoff, circuit breaker and timeouts. Since the `HttpClient`s live inside singletons, `SocketsHttpHandler` recycles connections every 5 min to avoid stale DNS. |
| **Logging with `[LoggerMessage]`** | Generated at compile time: no allocations or boxing, and structured messages. Prices always have 2 decimals, and the quote line says where the price stands in plain words (`above sell 22.67`, `neutral`). |
| **JSON with source generators** | The external APIs' responses are deserialized through generated contexts (`YahooJsonContext`, `BrapiJsonContext`), which is faster and trimming/AOT friendly. |
| **pt-BR formatting without ICU** | `InvariantGlobalization` reduces native dependencies (useful in containers). Amounts in reais and Brasília time (UTC−3, no daylight saving since 2019) are formatted by `BrazilianFormat`, in the Core, shared by the e-mail and the dashboard. |

### Extra features

| Decision | Reason |
| --- | --- |
| **One `StockMonitor` per stock** | Each stock keeps its own zone, so alerts never mix and each one becomes a separate e-mail. DI registers one monitor per `MonitoringTarget`, and the worker checks them in parallel so a slow provider for one stock does not delay the others. |
| **Several stocks as trios on the command line** | `PETR4 22.67 22.59 VALE3 61.50 60.80`: the single-stock format stays the same (it is the N = 1 case) and there is no new syntax to learn. `MonitoringTargets` guarantees the list is not empty and has no repeated ticker. |
| **Publisher/consumers instead of calling the e-mail directly** | The monitor only announces that an alert happened; whoever reacts decides what to do. This separates the decision to alert from the delivery, allows several channels without changing the Core, and keeps a slow SMTP server from delaying the quotes. |
| **Retry in the dispatcher, not in each consumer** | The retry policy (backoff, attempt limit) lives in one place and applies to every channel. The e-mail consumer records each attempt in the journal (failed or sent) and rethrows, so the dashboard shows the real history. |
| **brapi as backup provider** | brapi is B3-specific and kicks in when Yahoo fails (`QuoteProvider:Fallback`). |
| **Fallback as a decorator** | `FallbackQuoteProvider` implements `IQuoteProvider` and wraps two adapters, so the Core does not know a backup exists. It also asks the backup for a ticker the main provider does not know. If both fail, the error combines both messages and is unrecoverable only if both failures are (e.g. Yahoo down and brapi without a token keeps retrying). |
| **ASP.NET host in a console app** | It is still a console application, but the same `WebApplication` runs the worker, the dispatcher, the dashboard and the API, with unified DI, configuration, logging and lifetime. The API listens on `localhost` only. |
| **Blazor Server dashboard** | No Node.js and no front-end build: the UI is C#/Razor in the same process and the same `dotnet run`. Components subscribe to `IMonitoringJournal` directly on the server, and real-time updates come from Blazor's interactive connection, which reconnects on its own. |
| **Rebuild only what changed** | A journal event rebuilds the data of the stock it concerns; the other stocks keep their objects, and events about stocks that are not monitored cause no render. |
| **A clock only where it is needed** | The "verificado há X s" text is the only one that changes with time. The `RelativeTime` component owns its 1-second timer, so the clock ticking re-renders that text and never the page. |
| **Hand-made SVG chart** | The geometry lives in `PriceChartGeometry`, plain C# tested on its own. It avoids a JavaScript charting library and gives full control over the visuals: neutral band, reference lines, alert markers and tooltip. |
| **Bounded in-memory journal** | 2,000 quotes per stock (monitoring more stocks does not shorten each one's history), 500 notifications and the last failure of each stock. Each subscriber has its own bounded channel that drops its oldest events, so a slow dashboard never slows down monitoring. Nothing survives a restart; persistence is on the [roadmap](ROADMAP.md#persistence-and-data). |
| **Custom console formatter** | `CompactConsoleFormatter` prints one line per event (time, colored level, short class name and message) and, with more than one stock, a separator line at the end of each check. Colors only in a terminal: with redirected output or `NO_COLOR`, the output is plain text. |

## SOLID in practice

- **S**: each class has one reason to change. `CommandLineParser` only parses arguments, `AlertEmailComposer` only
  builds the text, `MailKitEmailSender` only speaks SMTP, `StockMonitor` only decides when to alert and
  `AlertDispatcher` only delivers messages.
- **O**: a new quote provider is a new `IQuoteSource`, and a new alert channel (Slack, SMS, Telegram) is a new
  `IAlertConsumer` registered in DI. Neither changes the Core.
- **L**: `YahooFinanceQuoteSource` and `BrapiQuoteSource` are interchangeable and follow the same exception
  contract. An adapter, with or without fallback, works with either of them.
- **I**: small, specific ports (`IQuoteProvider`, `IAlertPublisher`, `IAlertConsumer`, `IEmailSender`,
  `ISmtpClientFactory`).
- **D**: the Core depends only on abstractions. The concrete wiring happens in a single place (the
  `DependencyInjection` classes of Cli and Infrastructure).

## Testing strategy

- **Only the external boundaries are simulated**: the quote HTTP APIs (`FakeQuoteApi`, in Yahoo or brapi format and
  scriptable per ticker), the SMTP server (`FakeSmtpServer`), the clock (`FakeTimeProvider`) and the logs
  (`FakeLogger`, `CapturingLoggerProvider`). `MonitoringHarness` wires the real object graph using only these
  fakes. Published alerts stay in the real queue until the test delivers them, which keeps delivery deterministic.
- **Every class and record has its own tests**, including plain records and options classes; interfaces and enums
  are covered through the classes that use them.
- **Messaging**: `AlertDispatcherTests` covers parallel delivery, isolation between consumers, the exact
  5 s / 10 s / 20 s backoff, giving up after the last attempt, ordering, and an e-mail that fails and is then
  delivered.
- **End to end**: `EndToEndTests` runs the whole application (shipped `appsettings.json`, real host, `TestServer`)
  with only the quote APIs, the SMTP server and the clock faked: a full trading session tick by tick, a quote
  outage, an unknown ticker (exit code 3), several stocks, the brapi fallback, and the session read back through
  `/api/dashboard`. One API test also starts a real Kestrel server on a free local port.
- **Components**: bUnit renders the real dashboard against the real journal and checks live updates, the e-mail
  history, the detail drawer and its keyboard use, the chart tooltip, failure banners and, with several stocks, the
  stocks table, switching the selected stock and filtering the history.
- **Deterministic time**: `PeriodicTimer` receives the `TimeProvider`, so tests advance the clock instead of actually
  waiting. The same applies to the dispatcher backoff (`Task.Delay` with `TimeProvider`). Background work is awaited
  through signals and log entries, never fixed sleeps, and waits are written against the effect of a step (a zone
  change, a published alert), not against a state that may already hold. The whole suite (about 900 tests) runs in
  about 10 seconds.
- **Coverage**: about 99% of lines and branches of the three application assemblies (`tests/coverage.config` leaves
  out the tests and generated code). What is left is defensive code that cannot run, such as the default arm of a
  switch over a closed set of types.
