# Contributing to XlyFinance

Technical notes for people hacking on the add-in. If you just want to use it, the [README](README.md) is
enough.

## Tech stack and conventions

- Target framework: `net10.0-windows` for `XlyFinance.AddIn` and its tests, plain `net10.0` for
  `XlyFinance.Core` and its tests. Language: C# 14 (`LangVersion` is set centrally in `Directory.Build.props`).
- Key package: `ExcelDna.AddIn` / `ExcelDna.Integration` 1.9.0.
- Yahoo FX data is read from the public `v8/finance/chart` endpoint over `HttpClient` +
  `System.Text.Json`. `YahooFinanceApi` is deliberately **not** used (see
  [Known limitations and next steps](#known-limitations-and-next-steps)).
- The fetching UDFs return `object` through `ExcelAsyncUtil.RunTaskWithCancellation<object>`; their
  asynchronous helpers return `Task<object>`. Keep network work off Excel's calculation thread.
- Every UDF carries `[ExcelFunction]`.
- Clean separation between the external API (Yahoo HTTP, behind `IYahooHistoryClient`) and the Excel-DNA
  wrapper methods.
- `AGENTS.md` states the same rules for AI assistants working in this repo.

Before you commit: `dotnet build` must be clean and `dotnet test` must pass. No unit tests hit Yahoo.

## Versioning

The shared product version is set in `Directory.Build.props`, currently **1.1.0**. The SDK derives assembly
and file versions from it, keeping Core and the add-in aligned. Change this property rather than setting
separate versions in each project.

Use patch bumps for fixes, minor bumps for compatible features, and major bumps for breaking changes to
worksheet functions or their behavior. Version 1.1.0 introduces the shared history cache and `FXREFRESH`
command; existing formula signatures remain compatible. A version bump alone does not publish a release
or create a Git tag.

## Repository layout

```
src/XlyFinance.Core/        net10.0   domain + Yahoo access, no Excel references
  FxPair, FxQuery           input normalisation and validation
  FxHistoryService          fetch orchestration, cleaning, lookups, error mapping
  IFxHistoryProvider        abstraction over the data source
  CachedFxHistoryProvider  bounded window cache, shared fetches and refresh generation
  Providers/Yahoo/          IYahooHistoryClient + v8 chart HTTP client + retry provider
  Dates/, Excel/            loose date parsing, range flattening, Excel serial conversions
src/XlyFinance.AddIn/       net10.0-windows   Excel-DNA UDFs, argument coercion, async plumbing
  LastErrorObservable      latest diagnostic and live notifications to Excel subscribers
tests/XlyFinance.Tests/     net10.0   xUnit tests: domain, provider retries, and transport against canned payloads
tests/XlyFinance.AddIn.Tests/ net10.0-windows   diagnostic subscriptions without an Excel process
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
  IFxHistoryProvider                CachedFxHistoryProvider: reuse covering windows / pending fetches
                                    YahooFxHistoryProvider: retries transient failures on a miss
  IYahooHistoryClient               YahooChartHistoryClient: v8/finance/chart HTTP call
```

Bad arguments are rejected before any HTTP call (the UDF returns `#VALUE!` and records why). `FxQuery` validates
the pair and date window; the transport rechecks dates before padding the request bounds. Dates must be between
1970-01-01 and today (UTC), inclusive. This is a supported input range, not a promise of Yahoo coverage.

Service failures come back as an `FxError` — `InvalidRequest`, `NoData`, `ProviderUnavailable` or `Unexpected` — so the UDF can
pick an Excel error and record the detail for `FXLASTERROR()` and the trace log. A missing *individual* date is not
an error: `FXRANGE` returns `#N/A` in that cell and keeps the rest of the block intact.

Caller cancellation propagates to Excel-DNA without being recorded as a provider failure. A request timeout
with the caller's token still active is retried by the provider.

`FXLASTERROR()` subscribes to `LastErrorObservable` through `ExcelAsyncUtil.Observe`. Failures from any FX UDF,
including `FXSYMBOL`, update that shared diagnostic. New subscribers receive the latest message; disposing a
subscription stops its notifications. The message is global to the add-in, so concurrent requests can replace
one another's diagnostics, and a successful request does not clear it.

### Cache and refresh

`CachedFxHistoryProvider` decorates the Yahoo provider and is shared by both fetching UDFs across workbooks.
It has no extra package dependencies, timers, disk files or prefetching. A window records the full requested
coverage, including missing days; a same-pair subset can use it without another fetch. Partial overlaps are
fetched separately rather than stitched together.

- At most 64 completed windows and 100,000 observations are retained, evicting least recently used windows.
  A response larger than the observation limit is returned without being stored. Returned data is read-only
  and copied so neither the provider nor a caller can mutate cache contents.
- Historical windows expire after 24 hours; windows ending today (UTC) expire after five minutes. Expiry is
  checked on requests. `TimeProvider` makes expiry tests deterministic.
- Pending covering requests share a fetch. Each caller can cancel its own wait; the shared fetch continues
  under the Yahoo client's existing timeout/retry limits and may populate the cache even if all callers leave.
- Successful empty responses are cached. Exceptions are propagated without a cache entry.

`FXREFRESH` is an `[ExcelCommand]` under **Add-ins → Xly Finance → Refresh FX rates**, not a UDF. It clears
the cache, increments its generation, then calls Excel's `Application.CalculateFull()` on the main thread.
Both fetching UDFs include that generation in their async topic keys, so recalculation replaces old results
and errors without changing worksheet arguments. A clear detaches earlier fetches; their eventual completion
cannot restore stale cache entries or remove newer pending requests.

There are two caching layers: expiry in the provider cache does not invalidate an existing Excel-DNA topic.
Existing worksheet results change on explicit refresh or an argument change, not on a timer. `FXREFRESH`
affects every open workbook and triggers full calculation, including unrelated formulas. Check command/menu
registration and actual cell refresh in Windows Excel; unit tests cannot verify the native host behavior.

## Building and debugging (Windows)

Requirements:

- .NET 10 SDK; check `dotnet --list-sdks` from the terminal you will use to build.
- For IDE debugging, Visual Studio 2026 with the *.NET desktop development* workload (Community is fine).
  Use a version compatible with your installed SDK; see Microsoft's
  [SDK and Visual Studio support matrix](https://learn.microsoft.com/dotnet/core/porting/versioning-sdk-msbuild-vs).
- Excel (64-bit recommended; the build also emits a 32-bit `.xll`)

```
git clone <this repo>
cd xlyfinance
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
`Properties\launchSettings.json` during a Visual Studio build with an Excel launch profile for the installed
Excel bitness, so the unpacked add-in is loaded for the debug session and breakpoints in `FxFunctions` and `XlyFinance.Core`
are hit. The file is generated and git-ignored; a normal command-line build does not generate it by default.

If Excel starts but `=FXRATES(...)` says `#NAME?`, check that the add-in was loaded and the launch profile points
to the right `.xll`. Load the unpacked add-in through *File → Options → Add-ins → Excel Add-ins → Go… → Browse…*,
then use *Debug → Attach to Process… → EXCEL.EXE* — breakpoints work the same.

Either way, **close Excel before rebuilding**: the loaded `.xll` is locked and the build fails with a
file-in-use error.

### Visual Studio refuses `net10.0-windows`

Targeting .NET 10 in Visual Studio 2022 is unsupported. Use Visual Studio 2026 with a compatible .NET 10 SDK,
or build from the command line with the SDK. Keep the repository's target frameworks and C# 14 setting intact.

## Working off Windows

`XlyFinance.Core` and its tests are plain `net10.0`, so they build and run on any OS with the .NET 10 SDK.
`XlyFinance.AddIn` and its tests target `net10.0-windows`. The diagnostic subscription tests can run without
Excel, including on Linux; they do not exercise the native Excel host or COM registration.
The current solution builds and its tests run on Linux with the .NET 10 SDK using the same `dotnet build`
and `dotnet test` commands. Loading the resulting `.xll` and testing COM/RTD behavior still requires Windows Excel.

If a user-local SDK is installed but `dotnet` is not on `PATH`, invoke `~/.dotnet/dotnet` directly or add
`~/.dotnet` to your shell's `PATH`. On Linux, the SDK also needs its native dependencies, including ICU.
In a minimal environment without ICU, prefix the commands with `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`;
this can emit package locale warnings and does not verify behavior with full globalization support.

Line endings are normalised by `.gitattributes` (`* text=auto`, `.sln`/`.csproj` checked out as CRLF), and
`.editorconfig` carries the C# style rules the code already follows.

## Testing

`dotnet test` runs both test projects without calling Yahoo or starting Excel. Core tests cover date parsing,
date limits, query validation, history cleanup, lookups, retries, cancellation and chart JSON mapping, including
historical daylight-saving rules and missing/unknown timezones. Cache tests cover reuse across service calls,
expiry, size limits, shared fetches, cancellation isolation and clearing during outstanding requests. Fake
transports live in `tests/XlyFinance.Tests/Fakes/`. Add-in tests exercise diagnostic subscriptions with
recording observers and refresh ordering with an injected recalculation callback.

For a focused run:

```sh
dotnet test tests/XlyFinance.Tests/XlyFinance.Tests.csproj
dotnet test tests/XlyFinance.AddIn.Tests/XlyFinance.AddIn.Tests.csproj
```

Unit tests do not verify Excel function registration, spill behavior or COM/RTD delivery. Run the manual smoke
test below for those changes and record the outcome separately from automated results.

The RTD UDFs (`FXRATES`, `FXRANGE`, `FXLASTERROR`) are deliberately **not** marked `IsThreadSafe`. Excel-DNA's async
support is RTD based, and letting Excel call it from a worker thread races with the RTD COM registration, which
shows up as `RTD server ... could not be registered` in the diagnostics log (Excel-DNA issue #667). The slow work
already runs on the thread pool, so there is nothing to gain from the flag. `FXSYMBOL` keeps it.
`FXLASTERROR` uses `ExcelAsyncUtil.Observe` to push diagnostic changes to existing cells. Subscription tests
cover background updates, initial delivery of the latest error, multiple subscribers and disposal.

After changes to the Excel integration, smoke test in Windows Excel with automatic calculation enabled:

1. Load the newly built add-in into a fresh Excel process and enter `=FXLASTERROR()` in A1 and A2.
2. Enter `=FXRATES("ABCDEF","2024-01-01","2024-01-05")` in B1. When Yahoo rejects the ticker,
   both diagnostic cells should update without pressing F9 or editing them.
3. Delete A1, then trigger another failure with `=FXRANGE("EURUSD","not a date")` in B2.
   A2 should update; re-entering `=FXLASTERROR()` in A1 should show that latest failure immediately.
4. Check that dates before 1970 and future dates give `#VALUE!` with a date-limit diagnostic, while a normal
   historical request still returns rates. Use `=FXRATES("EURUSD","1969-12-31","2024-01-05")` and
   `=FXRATES("EURUSD","2024-01-01",TODAY()+2)` for the invalid cases.
5. Check `=FXRATES("EURUSD","2024-01-01","2024-01-10")` spills date/close columns and
   `=FXRANGE("EURUSD","2024-01-06")` returns `#N/A` for the Saturday.
6. In two workbooks, enter FX formulas with identical and overlapping windows. Choose
   **Add-ins → Xly Finance → Refresh FX rates** and confirm both workbooks' FX formulas fetch again without
   editing arguments. Check the `FXREFRESH` macro can also be assigned to a button. If using a debugger,
   break in `YahooChartHistoryClient.GetDailyBarsAsync` to count network requests; exact simultaneous windows
   should share one fetch, and completed covering windows should serve subsets without another fetch.
7. Disconnect the network, refresh and wait for a provider error; reconnect and refresh again. Confirm the
   failed formula recovers. Also refresh while a fetch is pending, checking that the new topic supplies the
   final result. Test with manual calculation enabled as well, since the command explicitly calculates.

## The data source

`GET https://query1.finance.yahoo.com/v8/finance/chart/{symbol}?interval=1d&events=history&period1=…&period2=…`

- No session, cookie or crumb: only a browser-style `User-Agent` header.
- The requested window is widened by a day at each end and trimmed locally, because Yahoo rounds bounds to
  whole sessions in the exchange's timezone.
- Dates must fall between 1970-01-01 and today (UTC), validated before HTTP requests or retry attempts.
- Bars are dated with the exchange's timezone (`meta.exchangeTimezoneName`) and the rules in force on that
  date, so a summer bar is not shifted by the exchange's current UTC offset. Closes are parsed straight into
  `decimal`; null or zero closes are dropped. Missing or unresolvable timezones cause a failure; the current
  `gmtoffset` is never substituted for historical timezone rules.
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
- No disk cache: history is shared in memory for the current Excel session only. New topics reuse covering
  cached windows when available. Persisting data across sessions would need a separate design.
- One pair per call. A multi-pair matrix function would be a separate UDF over the same service.
- The async value is cached by argument and refresh generation. F9 is not a refresh mechanism for cached
  history. After a transient outage, use `FXREFRESH` to replace failed async topics and retry.
- Close only. `YahooDailyBar`/`FxRate` are the extension points if OHLCV output is wanted later.
- Excel's 1900 date system is assumed (`ExcelSerialDate`). The 1904 (old Mac) system is not handled.
- Daily FX bars are dated in the exchange's timezone (Europe/London for FX, with historical daylight-saving
  rules). They are not shifted to the machine's local timezone.

User-visible consequences of these (older Excel, `#GETTING_DATA`, caching) are listed in the
[README](README.md#limitations).

## Reporting security issues

See [SECURITY.md](SECURITY.md) — please do not open a public issue for vulnerabilities.
