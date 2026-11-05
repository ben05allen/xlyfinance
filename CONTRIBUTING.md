# Contributing to XlyFinance

Technical notes for people hacking on the add-in. If you just want to use it, the [README](README.md) is
enough.

## Tech stack and conventions

- Target framework: `net10.0-windows` for `XlyFinance.AddIn`, plain `net10.0` for `XlyFinance.Core` and the
  tests. Language: C# 14 (`LangVersion` is set centrally in `Directory.Build.props`).
- Key package: `ExcelDna.AddIn` / `ExcelDna.Integration` 1.9.0.
- Yahoo FX data is read from the public `v8/finance/chart` endpoint over `HttpClient` +
  `System.Text.Json`. `YahooFinanceApi` is deliberately **not** used (see
  [Known limitations and next steps](#known-limitations-and-next-steps)).
- Async/await throughout; the UDFs return `Task<object>` and go through Excel-DNA's `ExcelAsyncUtil`.
- Every UDF carries `[ExcelFunction]`.
- Clean separation between the external API (Yahoo HTTP, behind `IYahooHistoryClient`) and the Excel-DNA
  wrapper methods.
- `AGENTS.md` states the same rules for AI assistants working in this repo.

Before you commit: `dotnet build` must be clean and `dotnet test` must pass (116 tests, none of them hit
Yahoo).

## Repository layout

```
src/XlyFinance.Core/        net10.0   domain + Yahoo access, no Excel references
  FxPair, FxQuery           input normalisation and validation
  FxHistoryService          fetch orchestration, cleaning, lookups, error mapping
  IFxHistoryProvider        abstraction over the data source
  Providers/Yahoo/          IYahooHistoryClient + v8 chart HTTP client + retry provider
  Dates/, Excel/            loose date parsing, range flattening, Excel serial conversions
src/XlyFinance.AddIn/       net10.0-windows   Excel-DNA UDFs, argument coercion, async plumbing
tests/XlyFinance.Tests/     net10.0   xUnit tests: domain, provider retries, and transport against canned payloads
```

The rule of thumb: `XlyFinance.AddIn` knows about Excel, `XlyFinance.Core` knows about data, and only
`YahooChartHistoryClient` knows about Yahoo's JSON. Swapping data source means
implementing `IFxHistoryProvider`; adding OHLC output means extending `FxRate` and `ExcelValues.ToRateArray`.

## How a request flows

```
FXRATES(...) / FXRANGE(...)         Excel-DNA UDF: coerce args, build FxQuery / date list
  ExcelAsyncUtil.RunTask…           async: returns #GETTING_DATA, then the array
  FxHistoryService                  GetDailyRatesAsync (clean, sort, dedupe) or
                                    GetClosesAsync (project closes onto the requested dates)
  IFxHistoryProvider                YahooFxHistoryProvider: retries transient failures
  IYahooHistoryClient               YahooChartHistoryClient: v8/finance/chart HTTP call
```

Bad arguments are rejected before any HTTP call (the UDF returns `#VALUE!` and says why). Everything after that
comes back as an `FxError` — `InvalidRequest`, `NoData`, `ProviderUnavailable` or `Unexpected` — so the UDF can
pick an Excel error and record the detail for `FXLASTERROR()` and the trace log. A missing *individual* date is not
an error: `FXRANGE` returns `#N/A` in that cell and keeps the rest of the block intact.

## Building and debugging (Windows)

Requirements:

- Visual Studio 2022 **17.14 or newer**, or **Visual Studio 2026** (Community is fine) — anything that can
  build .NET 10 projects. Include the *.NET desktop development* workload.
- .NET 10 SDK (bundled with VS 2026)
- Excel (64-bit recommended; the build also emits a 32-bit `.xll`)

```
git clone <this repo>
start XlyFinance.sln     # or open the folder in Visual Studio
dotnet build
dotnet test
```

`XlyFinance.AddIn` builds an Excel-DNA add-in into `src\XlyFinance.AddIn\bin\Debug\net10.0-windows\`:

- `XlyFinance64.xll` — unpacked 64-bit add-in, used for debugging (loads the DLLs next to it)
- `XlyFinance64.dna`, `XlyFinance.AddIn.dll`, `XlyFinance.Core.dll` — pieces the unpacked `.xll` needs
- `publish\XlyFinance64-packed.xll` — single-file 64-bit add-in, the thing you would hand to a colleague
- `XlyFinance.xll` / `publish\XlyFinance-packed.xll` — the same for 32-bit Excel

### F5 debugging

Set `XlyFinance.AddIn` as the startup project and press **F5**. Excel-DNA writes
`Properties\launchSettings.json` on build with an `Excel` profile that runs `EXCEL.EXE /x "XlyFinance64.xll"`,
so the unpacked add-in is loaded for the debug session and breakpoints in `FxFunctions` and `XlyFinance.Core`
are hit. The file is generated (and rewritten on every build), so it is git-ignored.

If Excel starts but `=FXRATES(...)` says `#NAME?`, Excel was launched in a different working directory and never
found the `.xll` (the profile passes a bare file name, and the build task rewrites it on every build). Load the
add-in by hand as above, then use *Debug → Attach to Process… → EXCEL.EXE* — breakpoints work the same.

Either way, **close Excel before rebuilding**: the loaded `.xll` is locked and the build fails with a
file-in-use error.

### Visual Studio refuses `net10.0-windows`

That TFM needs VS 2022 17.14+ (or VS 2026) and the .NET 10 SDK. Either update, or retarget all three projects
to `net8.0-windows` / `net8.0` (Excel-DNA 1.9 supports it; nothing else changes).

## Working off Windows

`XlyFinance.Core` and the tests are plain `net10.0`, so they build and run on any OS with the .NET 10 SDK.
Only `XlyFinance.AddIn` is Windows specific (`net10.0-windows`), because Excel-DNA builds the `.xll` there.
Building the add-in off Windows needs `dotnet build -p:EnableWindowsTargeting=true`.

Line endings are normalised by `.gitattributes` (`* text=auto`, `.sln`/`.csproj` kept as-is), and
`.editorconfig` carries the C# style rules the code already follows.

## Testing

`dotnet test` covers the domain, the retry policy and the JSON mapping; it does not call Yahoo. The live path
was checked by hand against `EURUSD=X`, including a weekend-only window and a bogus ticker. Fake transports
live in `tests/XlyFinance.Tests/Fakes/`.

Two async UDFs (`FXRATES`, `FXRANGE`) are deliberately **not** marked `IsThreadSafe`. Excel-DNA's async
support is RTD based, and letting Excel call it from a worker thread races with the RTD COM registration, which
shows up as `RTD server ... could not be registered` in the diagnostics log (Excel-DNA issue #667). The slow work
already runs on the thread pool, so there is nothing to gain from the flag. `FXSYMBOL`/`FXLASTERROR` are pure, so
they do keep it.

## The data source

`GET https://query1.finance.yahoo.com/v8/finance/chart/{symbol}?interval=1d&events=history&period1=…&period2=…`

- No session, cookie or crumb: only a browser-style `User-Agent` header.
- The requested window is widened by a day at each end and trimmed locally, because Yahoo rounds bounds to
  whole sessions in the exchange's timezone.
- Bars are dated with the exchange's timezone (`meta.exchangeTimezoneName`) and the rules in force on that
  date, so a summer bar is not shifted by the exchange's current UTC offset. Closes are parsed straight into
  `decimal`; null or zero closes are dropped.
- Responses are classified by HTTP status: 400/404/410 (or an inline error on a 200) means the request cannot
  succeed, so it fails immediately with Yahoo's message. Timeouts, throttling and 5xx are retried three times
  with a doubling delay (15 second timeout per attempt, so at worst ~45 seconds of `#GETTING_DATA`), and a
  timeout is reported as a timeout rather than as an Excel cancellation.
- A payload that does not have the expected shape (missing `close` array, different timestamp and close
  counts) is an error, never `#N/A` — a broken response must not look like a day without a fixing.

## Known limitations and next steps

Design-level constraints and the obvious follow-ups:

- Yahoo is an unofficial source: the `v8/finance/chart` endpoint needs no session but can throttle, and it is
  under no obligation to keep working. Everything is behind `IFxHistoryProvider` / `IYahooHistoryClient` so a
  fallback (ECB reference rates, another vendor) can be dropped in without touching the UDFs.
- `YahooFinanceApi` 2.3.3 is *not* used: its history method calls `v7/finance/download`, which wants a session
  cookie and crumb and now answers HTTP 401 for every symbol, valid or not (checked across pairs and ranges).
- No disk cache: every distinct argument set hits the network. An on-disk cache keyed by symbol + date would be
  the obvious next win for large sheets.
- One pair per call. A multi-pair matrix function would be a separate UDF over the same service.
- The async value is cached by argument; there is no explicit "refresh now" function yet (forcing a rebuild of
  the workbook is the current workaround).
- Close only. `YahooDailyBar`/`FxRate` are the extension points if OHLCV output is wanted later.
- Excel's 1900 date system is assumed (`ExcelSerialDate`). The 1904 (old Mac) system is not handled.
- Daily FX bars are dated in the exchange's timezone (Europe/London for FX, with historical daylight-saving
  rules). They are not shifted to the machine's local timezone.

User-visible consequences of these (older Excel, `#GETTING_DATA`, caching) are listed in the
[README](README.md#limitations).

## Reporting security issues

See [SECURITY.md](SECURITY.md) — please do not open a public issue for vulnerabilities.