# Stock Quote Alert

A C# console application that continuously monitors the price of a B3 stock and sends an e-mail advising a
**sale** when the price rises above a reference, or a **purchase** when it falls below another.

```bash
stock-quote-alert PETR4 22.67 22.59
#                 │     │     └─ buy reference: alert when the price falls BELOW it
#                 │     └─────── sell reference: alert when the price rises ABOVE it
#                 └───────────── B3 ticker
```

---

## Contents

- [Requirements coverage](#requirements-coverage)
- [Prerequisites](#prerequisites)
- [Quick start](#quick-start)
- [Configuration](#configuration)
- [Sending through Gmail](#sending-through-gmail)
- [Testing without sending real e-mails](#testing-without-sending-real-e-mails)
- [Running](#running)
- [Exit codes](#exit-codes)
- [How alerts work](#how-alerts-work)

---

## Requirements coverage

| Requirement | How it is met |
| --- | --- |
| E-mail alert when a B3 stock falls below one level or rises above another | Sell alert above the sell reference, buy alert below the buy reference. See [How alerts work](#how-alerts-work). |
| Console application, no graphical interface required | .NET console app (`stock-quote-alert`). |
| Called from the command line with 3 parameters: ticker, sell reference, buy reference | `stock-quote-alert PETR4 22.67 22.59`. Arguments are validated, with a clear message when they are wrong. |
| Reads the alert recipient and the SMTP settings from a configuration file | [`appsettings.json`](src/StockQuoteAlert.Cli/appsettings.json), sections `Email` and `Smtp`. Validated on startup. |
| Free choice of quote API | [Yahoo Finance](https://finance.yahoo.com) (no API key needed). |
| Keeps monitoring continuously while running | Checks immediately and then every 30 seconds (configurable) until stopped with Ctrl+C. |

## Prerequisites

Only the **.NET 10 SDK** (LTS). The exact version is pinned in [`global.json`](global.json).

macOS:

```bash
brew install dotnet
```

Windows/Linux: use the [official installer](https://dotnet.microsoft.com/download/dotnet/10.0).

## Quick start

1. Fill in the recipient and the SMTP account in
   [`src/StockQuoteAlert.Cli/appsettings.json`](src/StockQuoteAlert.Cli/appsettings.json):

   ```json
   "Email": {
     "Recipient": "destination@gmail.com",
     "SenderAddress": "your.address@gmail.com"
   },
   "Smtp": {
     "Host": "smtp.gmail.com",
     "Port": 587,
     "Security": "StartTls",
     "Username": "your.address@gmail.com",
     "Password": ""
   }
   ```

2. Keep the password out of the repository with user-secrets (for Gmail, see
   [Sending through Gmail](#sending-through-gmail)):

   ```bash
   cd src/StockQuoteAlert.Cli
   dotnet user-secrets set "Smtp:Password" "abcd efgh ijkl mnop"
   cd ../..
   ```

3. Run it:

   ```bash
   dotnet run --project src/StockQuoteAlert.Cli -- PETR4 22.67 22.59
   ```

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

## Sending through Gmail

Gmail requires **2-Step Verification** and an **app password**. The regular account password is rejected.

1. Turn on 2-Step Verification at <https://myaccount.google.com/security>.
2. Create an app password at <https://myaccount.google.com/apppasswords>.
3. Set `Smtp:Username` and `Email:SenderAddress` to your Gmail address, and `Smtp:Password` to the 16-character
   app password.

The defaults in `appsettings.json` (`smtp.gmail.com:587` with `StartTls`) already work for Gmail. The recipient can
be any address.

## Testing without sending real e-mails

Use a local SMTP server that captures messages, such as [Mailpit](https://mailpit.axllent.org):

```bash
docker run -d -p 1025:1025 -p 8025:8025 axllent/mailpit      # inbox at http://localhost:8025

Smtp__Host=localhost Smtp__Port=1025 Smtp__Security=None Smtp__RequireAuthentication=false \
Email__Recipient=destination@gmail.com Email__SenderAddress=alert@example.com \
dotnet run --project src/StockQuoteAlert.Cli -- PETR4 22.67 22.59
```

To see an alert right away, pick references around the current price, for example a sell reference just below it.

## Running

```bash
# Development (loads user-secrets)
dotnet run --project src/StockQuoteAlert.Cli -- PETR4 22.67 22.59

# Help
dotnet run --project src/StockQuoteAlert.Cli -- --help
```

Prices accept `.` or `,` as the decimal separator (`22.67` or `22,67`). The sell reference must be greater than
the buy reference. Stop the program with **Ctrl+C**.

### Publishing the executable

```bash
# Windows: publish/stock-quote-alert.exe
dotnet publish src/StockQuoteAlert.Cli -c Release -r win-x64 --self-contained -o publish
publish\stock-quote-alert.exe PETR4 22.67 22.59

# macOS (Apple Silicon)
dotnet publish src/StockQuoteAlert.Cli -c Release -r osx-arm64 --self-contained -o publish
./publish/stock-quote-alert PETR4 22.67 22.59
```

The output is self-contained: the target machine does not need .NET installed. `appsettings.json` is published next
to the executable. The published executable runs in the `Production` environment, where user-secrets are not
read, so set the password in `appsettings.json` or through the `Smtp__Password` environment variable.

The console shows one line per event, with the level colored on a terminal (plain text when redirected or with
`NO_COLOR` set):

```text
12:52:47 info  MonitoringWorker       Monitoring PETR4: sell above 22.67, buy below 22.59, every 30 seconds
12:52:48 info  StockMonitor           PETR4 quoted at 22.70 by Yahoo Finance (AboveSellThreshold)
12:52:49 info  EmailAlertNotifier     Sell alert e-mail for PETR4 sent to destination@gmail.com
12:52:49 info  StockMonitor           Sell alert sent for PETR4 at 22.70
```

## Exit codes

| Exit code | Meaning |
| --- | --- |
| `0` | Stopped normally (Ctrl+C) or `--help` |
| `1` | Invalid arguments |
| `2` | Invalid configuration |
| `3` | The stock could not be monitored anymore (unknown ticker or a request the provider rejects) |

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

If sending fails (SMTP unavailable, for example), the error is logged and the alert is retried on the **next
check**, with the price at that moment, until it is delivered. If the price returns between the references before
then, the alert is dropped, since it no longer applies.

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
