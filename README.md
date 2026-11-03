# Stock Quote Alert

A C# console application that continuously monitors the price of a B3 stock and sends an e-mail advising a
**sale** when the price rises above a reference, or a **purchase** when it falls below another.

```bash
stock-quote-alert PETR4 22.67 22.59
#                 │     │     └─ buy reference: alert when the price falls BELOW it
#                 │     └─────── sell reference: alert when the price rises ABOVE it
#                 └───────────── B3 ticker
```

To run it from the repository root:

```bash
dotnet run --project src/StockQuoteAlert.Cli -- PETR4 22.67 22.59
```

The executable is called `stock-quote-alert` (`stock-quote-alert.exe` on Windows), but it is not installed on your
`PATH`: run it through `dotnet run` as above, or by its path after building or publishing it (see
[Running](#running)).

---

## Contents

- [Requirements coverage](#requirements-coverage)
- [Prerequisites](#prerequisites)
- [Quick start](#quick-start)
  - [Sending real e-mails](#sending-real-e-mails) (default)
  - [Without sending real e-mails](#without-sending-real-e-mails)
  - [Choosing references to see an alert](#choosing-references-to-see-an-alert)
- [Configuration](#configuration)
- [Running](#running)
- [Exit codes](#exit-codes)
- [How alerts work](#how-alerts-work)
- [Extra features](#extra-features)
- [Further documentation](#further-documentation)

---

## Requirements coverage

| Requirement | How it is met |
| --- | --- |
| E-mail alert when a B3 stock falls below one level or rises above another | Sell alert above the sell reference, buy alert below the buy reference. See [How alerts work](#how-alerts-work). |
| Console application, no graphical interface required | .NET console app (`stock-quote-alert`). |
| Called from the command line with 3 parameters: ticker, sell reference, buy reference | `stock-quote-alert.exe PETR4 22.67 22.59` once published (see [Running](#running)). Arguments are validated, with a clear message when they are wrong. |
| Reads the alert recipient and the SMTP settings from a configuration file | [`appsettings.json`](src/StockQuoteAlert.Cli/appsettings.json), sections `Email` and `Smtp`. Validated on startup. |
| Free choice of quote API | [Yahoo Finance](https://finance.yahoo.com) (no API key needed). |
| Keeps monitoring continuously while running | Checks immediately and then every 30 seconds (configurable) until stopped with Ctrl+C. |

## Prerequisites

The **.NET 10 SDK** (LTS), version 10.0.403 or a later 10.0 SDK (pinned in [`global.json`](global.json)).
[Docker](https://www.docker.com/products/docker-desktop/) is only needed to
[run without sending real e-mails](#without-sending-real-e-mails).

macOS:

```bash
brew install --cask dotnet-sdk
```

Windows/Linux: use the [official installer](https://dotnet.microsoft.com/download/dotnet/10.0).

Check it in a new terminal; it should print `10.0.403` or later:

```bash
dotnet --version
```

## Quick start

All commands below run in a terminal at the **repository root** (the folder with `StockQuoteAlert.slnx`):

```bash
cd stock-quote-alert
```

There are two ways to run it:

- [Sending real e-mails](#sending-real-e-mails): the **default**. Alerts go through Gmail to a real inbox.
- [Without sending real e-mails](#without-sending-real-e-mails): alerts go to a local test server (Mailpit) and never
  leave your machine.

Either way, stop the program with **Ctrl+C**. While it runs, the dashboard is at <http://localhost:5080>.

### Sending real e-mails

This is what the defaults in [`appsettings.json`](src/StockQuoteAlert.Cli/appsettings.json) are for:
`smtp.gmail.com:587` with `StartTls`. Only the account and the recipient are missing.

**Once:**

1. Turn on 2-Step Verification at <https://myaccount.google.com/security>. Gmail rejects the regular account
   password.
2. Create an app password at <https://myaccount.google.com/apppasswords> (16 characters).
3. Save the account and the recipient with user-secrets, which keeps them out of the repository:

   ```bash
   dotnet user-secrets set "Smtp:Username"       "your.address@gmail.com" --project src/StockQuoteAlert.Cli
   dotnet user-secrets set "Smtp:Password"       "abcd efgh ijkl mnop"    --project src/StockQuoteAlert.Cli
   dotnet user-secrets set "Email:SenderAddress" "your.address@gmail.com" --project src/StockQuoteAlert.Cli
   dotnet user-secrets set "Email:Recipient"     "destination@gmail.com"  --project src/StockQuoteAlert.Cli
   ```

   - `Smtp:Username` is the SMTP login, not a display name. On Gmail it is the full address.
   - `Email:SenderAddress` must be that same address, because Gmail only sends as the authenticated account. The
     display name is `Email:SenderName` (`Stock Quote Alert` by default).
   - The recipient can be any address.
   - `dotnet user-secrets list --project src/StockQuoteAlert.Cli` shows what is saved, password included.

**Every time:**

```bash
dotnet run --project src/StockQuoteAlert.Cli -- PETR4 22.67 22.59
```

User-secrets are only read in the `Development` environment, which `dotnet run` uses (see [Running](#running)). To
run the built executable with them, see [The built executable](#the-built-executable).

### Without sending real e-mails

[Mailpit](https://mailpit.axllent.org) is a local SMTP server that captures messages and shows them in a web page.
It runs in [Docker](https://www.docker.com/products/docker-desktop/).

**Once:**

1. Start Docker Desktop (macOS: `open -a Docker`) and wait until `docker info` answers without an error.
2. Create and start the Mailpit container:

   ```bash
   docker run -d --name mailpit -p 1025:1025 -p 8025:8025 axllent/mailpit
   ```

**Every time:**

1. Start Docker Desktop if it is not running, then the container (it prints `mailpit`, even if it was already
   running):

   ```bash
   docker start mailpit
   ```

2. Check that it is up: the `STATUS` column should read `Up ... (healthy)`, and <http://localhost:8025> should show
   the Mailpit inbox.

   ```bash
   docker ps --filter name=mailpit
   ```

3. Run the app against it (macOS / Linux):

   ```bash
   Smtp__Host=localhost Smtp__Port=1025 Smtp__Security=None Smtp__RequireAuthentication=false \
   Email__Recipient=destination@example.com Email__SenderAddress=alert@example.com \
   dotnet run --project src/StockQuoteAlert.Cli -- PETR4 22.67 22.59
   ```

   On Windows (PowerShell), the variables stay set for the rest of the session, so use a terminal you close
   afterwards:

   ```powershell
   $env:Smtp__Host = "localhost"; $env:Smtp__Port = "1025"; $env:Smtp__Security = "None"
   $env:Smtp__RequireAuthentication = "false"
   $env:Email__Recipient = "destination@example.com"; $env:Email__SenderAddress = "alert@example.com"
   dotnet run --project src/StockQuoteAlert.Cli -- PETR4 22.67 22.59
   ```

4. The e-mails appear at <http://localhost:8025>.

When you are done, `docker stop mailpit` stops the container. `docker rm mailpit` also deletes it and the captured
e-mails.

Environment variables override user-secrets (see [Configuration](#configuration)). This path is safe even with the
Gmail user-secrets saved: the variables replace the server and turn authentication off, so the password is never
sent. On macOS / Linux, keep the variables on the same line as the command and do not `export` them, or every later
run in that terminal, including the real one, goes to Mailpit.

### Choosing references to see an alert

A **sell** alert goes out when the price is **above** the sell reference, and a **buy** alert when it is **below**
the buy reference. The sell reference must be greater than the buy reference. With the price at, say, R$ 50.71:

| To see | Arguments | Why |
| --- | --- | --- |
| A sell alert | `PETR4 30 20` | 50.71 is above 30 |
| A buy alert | `PETR4 70 60` | 50.71 is below 60 |
| No alert | `PETR4 1000 1` | 50.71 is between 1 and 1000 |

The first log line after startup shows the current price, for example
`PETR4 quoted at 50.71 by Yahoo Finance (above sell 30.00)`. Adjust the numbers to it.

## Configuration

The configuration file is [`src/StockQuoteAlert.Cli/appsettings.json`](src/StockQuoteAlert.Cli/appsettings.json).
It is copied next to the executable and read from there, whatever the current directory. Each value can be
overridden, from lowest to highest priority, by:

1. `appsettings.{Environment}.json`
2. **user-secrets**, only in `Development` (which `dotnet run` uses through
   [`launchSettings.json`](src/StockQuoteAlert.Cli/Properties/launchSettings.json))
3. **environment variables**, using `__` as the section separator: `Smtp__Password`, `Email__Recipient`…

| Key | Required | Default | Description |
| --- | --- | --- | --- |
| `Email:Recipient` | ✓ | — | Address that receives the alerts |
| `Email:SenderAddress` | ✓ | — | Sender address (on Gmail, the authenticated account itself) |
| `Email:SenderName` | | `Stock Quote Alert` | Sender display name |
| `Email:TradingPlatformUrl` | | `https://homebroker.example.com/` | Trading platform behind the e-mail's "Vender/Comprar agora" button (the default is fictitious; must end with `/`) |
| `Smtp:Host` | ✓ | `smtp.gmail.com` | SMTP server |
| `Smtp:Port` | | `587` | Port (1–65535) |
| `Smtp:Security` | | `StartTls` | `StartTls`, `SslOnConnect` or `None` |
| `Smtp:RequireAuthentication` | | `true` | Turn off only for local test servers such as Mailpit |
| `Smtp:Username` / `Smtp:Password` | ✓ if authenticated | — | SMTP credentials. **Never** commit the password |
| `Smtp:CheckCertificateRevocation` | | `false` | Checks the server certificate through OCSP/CRL. Turn on only if the host can reach the revocation servers |
| `Smtp:Timeout` | | `00:00:30` | Between 1 s and 5 min |
| `QuoteProvider:Yahoo:BaseUrl` | | `https://query1.finance.yahoo.com/` | Yahoo Finance API root (must end with `/`) |
| `QuoteProvider:Yahoo:UserAgent` | | a browser-like string | Yahoo rejects requests without a User-Agent |
| `Monitoring:PollingInterval` | | `00:00:30` | Time between quote checks (5 s to 1 day) |

`Smtp:Security` `None` is only accepted when `Smtp:RequireAuthentication` is `false`, so the password is never sent
unencrypted.

**Every option is validated on startup.** If something is missing or invalid, the application does not start
monitoring. It lists every problem and exits with code `2`:

```text
error: invalid configuration
  - Email:Recipient is required.
  - Email:SenderAddress is required.
  - Smtp:Username is required when Smtp:RequireAuthentication is true.
  - Smtp:Password is required when Smtp:RequireAuthentication is true (set it through user-secrets or the Smtp__Password environment variable).

Set the values in appsettings.json, user-secrets or environment variables (e.g. Smtp__Password).
```

## Running

Run every command from the repository root.

### With `dotnet run` (development)

One stock:

```bash
dotnet run --project src/StockQuoteAlert.Cli -- PETR4 22.67 22.59
```

Several stocks (repeat the three values):

```bash
dotnet run --project src/StockQuoteAlert.Cli -- PETR4 22.67 22.59 VALE3 61.50 60.80
```

Help:

```bash
dotnet run --project src/StockQuoteAlert.Cli -- --help
```

- Everything after `--` goes to the application; without arguments, `dotnet run` uses the ones in
  [`launchSettings.json`](src/StockQuoteAlert.Cli/Properties/launchSettings.json) (`PETR4 22.67 22.59`).
- It runs in the `Development` environment, so the values saved with `dotnet user-secrets` are read.
- The dashboard is at <http://localhost:5080> while it runs.

Prices accept `.` or `,` as the decimal separator (`22.67` or `22,67`). The sell reference must be greater than
the buy reference. Stop the program with **Ctrl+C**; with the dashboard open in a browser, stopping can take up to
30 seconds.

### The built executable

Build it:

```bash
dotnet build
```

Run it on macOS / Linux:

```bash
./src/StockQuoteAlert.Cli/bin/Debug/net10.0/stock-quote-alert PETR4 22.67 22.59
```

Run it on Windows (PowerShell):

```powershell
.\src\StockQuoteAlert.Cli\bin\Debug\net10.0\stock-quote-alert.exe PETR4 22.67 22.59
```

Run directly, the executable uses the `Production` environment, where user-secrets are **not** read: put the
values in `appsettings.json`, set them as environment variables (`Smtp__Password`, `Email__Recipient`…), or run it
in `Development`:

```bash
DOTNET_ENVIRONMENT=Development ./src/StockQuoteAlert.Cli/bin/Debug/net10.0/stock-quote-alert PETR4 22.67 22.59
```

### Publishing the executable

Windows (PowerShell): publish, then run `publish\stock-quote-alert.exe`:

```powershell
dotnet publish src/StockQuoteAlert.Cli -c Release -r win-x64 --self-contained -o publish
```

```powershell
.\publish\stock-quote-alert.exe PETR4 22.67 22.59
```

macOS (Apple Silicon): publish, then run `publish/stock-quote-alert`. On Intel Macs use `-r osx-x64`, and on
Linux `-r linux-x64`.

```bash
dotnet publish src/StockQuoteAlert.Cli -c Release -r osx-arm64 --self-contained -o publish
```

```bash
./publish/stock-quote-alert PETR4 22.67 22.59
```

The output is self-contained: the target machine does not need .NET installed. `appsettings.json` is published next
to the executable. The published executable runs in the `Production` environment, where user-secrets are not
read, so set the password in `appsettings.json` or through the `Smtp__Password` environment variable.

The console shows one line per event, with the level colored on a terminal (plain text when redirected or with
`NO_COLOR` set):

```text
12:52:47 info  MonitoringWorker       Monitoring PETR4: sell above 22.67, buy below 22.59, every 30 seconds
12:52:48 info  StockMonitor           PETR4 quoted at 22.70 by Yahoo Finance (above sell 22.67)
12:52:48 info  StockMonitor           Sell alert raised for PETR4 at 22.70
12:52:49 info  EmailAlertConsumer     Sell alert e-mail for PETR4 sent to destination@gmail.com
```

## Exit codes

| Exit code | Meaning |
| --- | --- |
| `0` | Stopped normally (Ctrl+C) or `--help` |
| `1` | Invalid arguments |
| `2` | Invalid configuration |
| `3` | No stock can be monitored anymore: every ticker is unknown, or the provider rejects the requests (with several stocks, only when none is left) |

Temporary failures (network, HTTP 5xx, rate limiting) do **not** stop the program: each HTTP call is retried with
backoff, and if it still fails the error is logged and the next check runs as usual.

## How alerts work

Using the PETR4 example from the requirements, with the sell reference at R$ 22.67 and the buy reference at
R$ 22.59:

- price **above the sell reference** → e-mail advising a **sale**;
- price **below the buy reference** → e-mail advising a **purchase**;
- price between the two references → neutral zone, no e-mail.

The alert fires on the **transition** into a zone, not on every check. Without this, a price sitting above the
sell reference would send one e-mail every 30 seconds:

| Previous → current price | E-mail |
| --- | --- |
| neutral → above sell | **SELL** |
| neutral → below buy | **BUY** |
| above sell → above sell | — |
| above sell → neutral → above sell | **SELL** (again) |
| above sell → below buy | **BUY** |

The references are exclusive: a price exactly equal to a reference counts as neutral.

Sending happens in the background, so a slow or unavailable SMTP server never delays the price checks. If sending
fails, the same e-mail is retried after 5 s, 10 s and 20 s, up to 4 attempts. If every attempt fails, the error is
logged and a new e-mail is only sent when the price crosses a reference again.

The e-mail is written in Portuguese, for a Brazilian investor, with prices in R$ and times in Brasília time. It is
sent in two versions (`multipart/alternative`): HTML, with a card colored by alert type and a button to the trading
platform, and plain text for clients that do not show HTML. The plain-text version of a sell alert:

```text
Assunto: [VENDA] PETR4 subiu para R$ 22,70, 0,13% acima da referência de venda

Olá,

PETR4 superou o seu preço de referência para venda.

  Cotação atual:          R$ 22,70
  Referência de venda:    R$ 22,67
  Diferença:              R$ 0,03 (0,13% acima)
  Cotado em:              29/10/2026 14:30 (horário de Brasília)

Recomendação: VENDA. O ativo está sendo negociado acima do preço-alvo que você definiu; pode ser um bom momento para realizar o lucro.

Vender agora: https://homebroker.example.com/negociar?ativo=PETR4&operacao=venda&preco=22.70

Para evitar e-mails repetidos, um novo alerta só será enviado quando a cotação voltar a ultrapassar uma das referências configuradas: subir acima da referência de venda ou cair abaixo da referência de compra.

--
Stock Quote Alert
Alerta automático gerado a partir das referências configuradas por você. Não constitui recomendação de investimento.
```

---

## Extra features

Everything below goes beyond the requirements. None of it is needed to use the core feature, which works with the
default settings.

### Several stocks at once

Repeat the `TICKER SELL BUY` trio to monitor more than one stock:

```bash
dotnet run --project src/StockQuoteAlert.Cli -- PETR4 22.67 22.59 VALE3 61.50 60.80 ITUB4 40.00 38.50
```

Each stock has its own monitor and its own zone. All stocks are checked in parallel on every cycle, and each alert
is a **separate e-mail** about that stock only. A ticker may appear only once, and argument errors say which stock
they refer to (`Stock 2 (VAL3 61.50 60.80): Invalid ticker 'VAL3'…`).

An unrecoverable failure (unknown ticker, for example) stops **only** the affected stock; the others keep being
monitored. The program exits with code `3` only when no stock is left.

### Real-time dashboard

While the program runs, it also serves a web dashboard at <http://localhost:5080> (key `Urls`). It is built with
**Blazor Server**, inside the same .NET process: no Node.js, no front-end build.

<!-- Screenshot placeholder: save a capture of the dashboard as docs/dashboard.png and replace the line below with
![Dashboard](docs/dashboard.png) -->
> *Dashboard screenshot coming soon (`docs/dashboard.png`).*

The dashboard shows:

- with more than one stock, a **monitored stocks** table: last price, zone, distance to the nearest reference,
  references, e-mails and status. Clicking a row (or Enter/Space) selects the stock shown below;
- last price, current zone and how long ago it was checked;
- sell and buy references, and the number of e-mails sent or failed for the selected stock;
- a **live chart** with the sell (blue) and buy (red) lines, the neutral band and markers where an e-mail was sent,
  with a toggle to a table view;
- the **e-mail history** with date and time, action, stock, price, reference, recipient and status. Clicking a row
  (or Enter/Space) opens the full e-mail and, if any, the delivery error. With several stocks, the history can be
  filtered by stock;
- failure banners (temporary, or one that stopped a stock), explained in plain language, with the provider's
  technical message under "Detalhes técnicos".

The page updates live as quotes, e-mails and failures are recorded. The dashboard text is in Portuguese. There is
also a JSON API:

| Method | Route | Description |
| --- | --- | --- |
| `GET` | `/api/health` | Health check |
| `GET` | `/api/dashboard` | Snapshot: stocks and configuration, quotes, notifications and the last failure of each stock |

### Backup quote provider

| Provider | API key | Notes |
| --- | --- | --- |
| **Yahoo Finance** (default) | No | Unofficial public API; B3 quotes are delayed by about 15 min |
| **brapi.dev** (backup) | Optional | B3-specific; without a token, some tickers are refused |

Yahoo is tried first on every check. If it fails, the quote comes from brapi, and a failure is only recorded when
both fail; the combined error stays recoverable if either failure is. Whatever the source, the quote goes through the
same normalization (price rounded to cents, time in UTC), and the dashboard and the log show where each quote came
from.

| Key | Default | Description |
| --- | --- | --- |
| `QuoteProvider:Provider` | `Yahoo` | `Yahoo` or `Brapi` |
| `QuoteProvider:Fallback` | `Brapi` | Provider used when the main one fails; must differ from it. Leave empty to turn it off |
| `QuoteProvider:Brapi:BaseUrl` | `https://brapi.dev/` | brapi API root (must end with `/`) |
| `QuoteProvider:Brapi:Token` | — | [brapi.dev](https://brapi.dev) token, sent as a Bearer header. Keep it in user-secrets or `QuoteProvider__Brapi__Token` |

### Alert messaging (publisher/consumers)

The monitor does not send e-mails directly. When a stock enters an alert zone, it **publishes** a
`PriceAlertRaised` message to an internal queue and goes back to checking quotes. A background dispatcher reads the
queue and delivers the message to every registered **consumer**, and each consumer decides what to do with it:

| Consumer | What it does |
| --- | --- |
| `EmailAlertConsumer` | Builds and sends the e-mail and records each attempt in the dashboard history |
| `SlackAlertConsumer` | Example: only logs that it received the alert (nothing is posted) |
| `SmsAlertConsumer` | Example: only logs that it received the alert (no SMS is sent) |

Consumers are independent: if the e-mail fails, Slack and SMS still receive the alert, and only the e-mail is
retried. A new channel is a class that implements `IAlertConsumer`, registered in dependency injection. See
[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md#alert-messaging).

### HTML e-mail

The e-mail is sent in two versions (`multipart/alternative`): HTML, with a card colored by alert type and a button
to the trading platform (`Email:TradingPlatformUrl`, a fictitious default), and plain text, which is also shown on
the dashboard.

### Readable console logs

One line per event: time, level, source class and message. The level is colored when the output is a terminal;
redirected to a file, or with `NO_COLOR` set, the output is plain text. With more than one stock, a dashed line
closes each check:

```text
14:29:35 info  MonitoringWorker       Monitoring PETR4: sell above 22.67, buy below 22.59, every 30 seconds
14:29:35 info  MonitoringWorker       Monitoring VALE3: sell above 61.50, buy below 60.80, every 30 seconds
14:29:35 info  StockMonitor           VALE3 quoted at 60.60 by Yahoo Finance (below buy 60.80)
14:29:35 info  StockMonitor           PETR4 quoted at 22.63 by Yahoo Finance (neutral)
14:29:35 info  StockMonitor           Buy alert raised for VALE3 at 60.60
------------------------------------------------------------------------
14:29:35 info  SlackAlertConsumer     Buy alert for VALE3 at 60.60 received; posting to Slack is not implemented (example consumer)
14:29:35 info  SmsAlertConsumer       Buy alert for VALE3 at 60.60 received; sending SMS is not implemented (example consumer)
14:29:36 info  EmailAlertConsumer     Buy alert e-mail for VALE3 sent to destination@gmail.com
```

Consumer lines may appear after the separator because delivery happens in the background, outside the check.

### Quality

Tests (about 900: unit, components with bUnit, and end to end with TestServer):

```bash
dotnet test
```

Formatting (`.editorconfig`):

```bash
dotnet format --verify-no-changes
```

Coverage, written to `TestResults/coverage.cobertura.xml` (about 99% of lines and branches):

```bash
dotnet test -- --coverage --coverage-settings "$PWD/tests/coverage.config" --coverage-output-format cobertura --coverage-output coverage.cobertura.xml
```

The build treats **warnings as errors**, with the .NET analyzers at `latest-recommended` and the `.editorconfig`
style rules enforced at compile time. The CI ([`.github/workflows/ci.yml`](.github/workflows/ci.yml)) runs
**Format**, **Build** and **Tests** as parallel jobs, and a **Quality check** job that passes only when all three
pass.

## Further documentation

- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md): layers, the flow of a check, alert messaging, technical decisions
  and testing strategy
- [docs/ROADMAP.md](docs/ROADMAP.md): possible next steps, grouped by theme and prioritized
