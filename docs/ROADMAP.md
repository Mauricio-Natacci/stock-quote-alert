# Roadmap

Suggested next steps, grouped by theme and prioritized by impact × effort (**P1** = natural next step, **P3** = when
there is demand).

## Features

| Priority | Item | Details |
| --- | --- | --- |
| P1 | **Trading hours** | Pause checks outside B3 trading hours (10:00–17:00 on business days, excluding holidays), reducing calls and avoiding stale quotes. |
| P1 | **Cooldown / hysteresis** | Avoid bursts of e-mails when the price oscillates right on the reference (e.g. require a minimum move, or a minimum interval between alerts of the same kind). |
| P2 | **Real channels besides e-mail** | Messaging already delivers each alert to every `IAlertConsumer`, and Slack and SMS exist as examples that only log. What is missing is the actual integrations (Slack webhook, an SMS gateway such as Twilio, Telegram, Teams) and showing every channel's history on the dashboard, not just e-mail. |
| P2 | **Channels from configuration** | Turn consumers on and off and set recipients per channel in `appsettings.json` (e.g. `Alerts:Consumers:Slack:Enabled`, a list of e-mails), without recompiling. |
| P2 | **Manage references from the UI** | Change the references without restarting the process (`IOptionsMonitor` + a `PUT` endpoint). |
| P3 | **Stocks from configuration or from the UI** | Several stocks are already supported on the command line. The next step is reading a `{ ticker, sell, buy }` list from the configuration file, or adding and removing stocks from the dashboard without restarting. |
| P3 | **Mini chart in the e-mail** | The e-mail already has an HTML version; it could include a small chart of the latest quotes and a link to the dashboard. |
| P3 | **Fallback with memory** | Today the main provider is tried on every check. With Yahoo down, each check waits for its retries before going to brapi; the backup could be used alone for a few minutes after a failure. |
| P3 | **Clearer brapi errors** | Without a token, brapi answers HTTP 401 for an unknown ticker too, so the message suggests checking the token even when the ticker is simply wrong. A token-less request for a known test ticker could tell the two cases apart. |
| P3 | **Percentage alerts** | Relative references, such as "moved ±2% today" or "fell 5% since the open". |

## Messaging

| Priority | Item | Details |
| --- | --- | --- |
| P1 | **Message broker** | Replace `InMemoryAlertQueue` and `AlertDispatcher` with RabbitMQ, Azure Service Bus or Kafka. Queued alerts would survive restarts, and consumers could run in other processes. The Core and the consumers do not change. |
| P2 | **Dead-letter queue** | Today an alert that runs out of its 4 attempts is dropped with an error log. A dead-letter queue would allow inspecting and resending those alerts. |
| P2 | **Idempotent consumers** | With a broker, delivery becomes "at least once". The `PriceAlertRaised` `Id` lets each consumer skip messages it has already handled. |
| P3 | **One queue per consumer** | The dispatcher handles one alert at a time, so an e-mail being retried (up to 35 s) holds back the next alert for every channel. A queue per consumer would keep a failing channel from delaying the others. |

## Persistence and data

| Priority | Item | Details |
| --- | --- | --- |
| P1 | **Persist the history** | Replace `InMemoryMonitoringJournal` with SQLite or PostgreSQL through EF Core, keeping the same `IMonitoringJournal` port. The history would survive restarts. |
| P2 | **Persist the zone** | Today, restarting with the price already past a reference sends a new alert. Persisting the last notified zone avoids that duplicate. |
| P2 | **Alert outbox** | Store the `PriceAlertRaised` in the database in the same transaction as the zone, and publish from that table. Together with a broker, this guarantees no alert is lost between the decision and the delivery. |
| P3 | **Historical series** | A time-series database (TimescaleDB) for multi-day charts and backtesting of the references. |

## Observability and operations

| Priority | Item | Details |
| --- | --- | --- |
| P1 | **Real health checks** | `AddHealthChecks()` checking the quote provider and the SMTP connection (`/health/ready` and `/health/live`). |
| P2 | **Faster shutdown** | Stop monitoring as soon as shutdown starts (link the worker to `ApplicationStopping`) and lower `HostOptions.ShutdownTimeout` from the default 30 s, so an open dashboard tab cannot keep the process, and its checks, running after Ctrl+C. |
| P2 | **OpenTelemetry** | Traces of the HTTP/SMTP calls, metrics (quotes per minute, alerts sent or failed, provider latency) and structured logs exported through OTLP (Grafana, Seq, Application Insights). |
| P2 | **Dashboard scale** | Blazor Server keeps one connection per open tab. For many users: Azure SignalR Service, or sticky sessions behind a load balancer. |
| P3 | **Alerts about the monitor itself** | Send an e-mail if monitoring stops or the provider is unavailable for more than N minutes. |

## Security

| Priority | Item | Details |
| --- | --- | --- |
| P1 | **Production secrets** | Azure Key Vault, AWS Secrets Manager or Docker/Kubernetes secrets instead of plain-text environment variables. |
| P1 | **Protect the dashboard API** | Today it only listens on `localhost`. To expose it on a network: authentication (OIDC or an API key), HTTPS, restricted CORS and rate limiting (`AddRateLimiter`). |
| P2 | **OAuth2 for Gmail** | Use OAuth2 (XOAUTH2, supported by MailKit) instead of an app password. |
| P2 | **Dependency scanning** | Dependabot/Renovate and `dotnet list package --vulnerable` in CI. |

## Quality and engineering

| Priority | Item | Details |
| --- | --- | --- |
| P1 | **Coverage in CI** | Coverage can already be measured locally (`tests/coverage.config`). The Tests job still needs to collect it, fail below a minimum threshold and publish the report (Codecov or ReportGenerator). |
| P2 | **Contract tests against the external APIs** | Nightly tests against the real Yahoo and brapi APIs to catch changes in the response format, kept apart from the fast suite. |
| P2 | **Browser E2E tests** | Playwright for .NET against the app with `FakeQuoteApi` and Mailpit, covering what bUnit cannot (layout, circuit reconnection, the drawer's focus handling). |
| P2 | **Mutation testing** | Stryker.NET to measure the quality of the tests, not only the coverage. |
| P3 | **API documentation** | Generate OpenAPI (`Microsoft.AspNetCore.OpenApi`) for the `/api/dashboard` JSON API, making integrations easier. |

## UI

| Priority | Item | Details |
| --- | --- | --- |
| P2 | **Light theme** | The palette has been chosen for the dark theme. A light theme needs matching tones and a theme switcher. |
| P2 | **Chart time range** | Last hour / day / all, plus zoom and brush. |
| P2 | **Browser notifications** | Notification API, through JS interop, when an alert fires with the tab open. |
| P3 | **Mobile** | Layout optimized for phones. |
| P3 | **i18n** | Texts moved to `.resx` files with `IStringLocalizer` (pt-BR / en). |
