# XlyFinance

An Excel add-in (Excel-DNA, .NET 10) that loads daily FX rates from Yahoo Finance.

You give it a pair, a start date and an end date; it pulls the daily closing rates for that window and
spills two columns into the sheet: **date** and **close**. Both ends of the range are inclusive.

```
=FXRATES("EURUSD", "2024-01-01", "2024-01-10")

| 45292 | 1.1055831909179688 |   <- date serial, format column A as a date
| 45293 | 1.1038745641708374 |
| 45294 | 1.0941756963729858 |
| ...   | ...                |
```

(Those are real values: eight rows for that window, because 6 and 7 January 2024 were a weekend.)

## Functions

All three are in the *Xly Finance* category of Excel's function wizard.

| Function | Returns |
| --- | --- |
| `FXRATES(pair, start_date, end_date)` | Spilled 2-column array: date serial, close. `#N/A` when Yahoo has no observations in the window, `#VALUE!` when the request is invalid or Yahoo could not be reached. |
| `FXSYMBOL(pair)` | The Yahoo ticker for a pair, e.g. `FXSYMBOL("eur/usd")` → `EURUSD=X`. Handy for sanity checks. |
| `FXLASTERROR()` | The message behind the most recent `FXRATES` failure. Excel errors such as `#VALUE!` carry no detail, this does. |

Notes on the data:

- Bars are **daily closes** as reported by Yahoo Finance. Weekends and most market holidays have no bar at
  all — the array is not padded or forward-filled, so row counts can be shorter than the date range.
- The pair is normalised: `EURUSD`, `eur/usd`, `EUR_USD` and `EURUSD=X` all resolve to `EURUSD=X`.
- Recalculation with identical arguments reuses the value already fetched (Excel-DNA async/RTD caching), so
  pressing F9 does not hammer Yahoo. Change an argument, or use the same arguments in a fresh workbook, to
  force a new request.
- Dates are returned as Excel serial numbers, so charts and date maths work. Format the column as a date
  (`Ctrl+1`) to see `2024-01-01` instead of `45292`.
- A market holiday that Yahoo still reports a bar for (usually a repeat of the previous close) is kept as
  reported; nothing is inferred or forward-filled.
- One pair per call, and the result is a plain array — no formatting, no table, no formulas written into the
  sheet.

### Where the data comes from

`GET https://query1.finance.yahoo.com/v8/finance/chart/{symbol}?interval=1d&events=history&period1=…&period2=…`

- No session, cookie or crumb: only a browser-style `User-Agent` header.
- The requested window is widened by a day at each end and trimmed locally, because Yahoo rounds bounds to
  whole sessions in the exchange's timezone.
- Bars are mapped to dates using the response's `gmtoffset`, parsed straight into `decimal`, and null or zero
  closes are dropped.
- 30 second timeout, three attempts with a doubling delay for transport failures; unknown tickers fail
  immediately with Yahoo's own message.

## Layout

```
src/XlyFinance.Core/        net10.0   domain + Yahoo access, no Excel references
  FxPair, FxQuery           input normalisation and validation
  FxHistoryService          fetch orchestration, cleaning, error mapping
  IFxHistoryProvider        abstraction over the data source
  Providers/Yahoo/          IYahooHistoryClient + v8 chart HTTP client + retry provider
  Dates/, Excel/            loose date parsing, Excel serial conversions
src/XlyFinance.AddIn/       net10.0-windows   Excel-DNA UDFs, argument coercion, async plumbing
tests/XlyFinance.Tests/     net10.0   xUnit tests: domain, provider retries, and transport against canned payloads
```

The rule of thumb: `XlyFinance.AddIn` knows about Excel, `XlyFinance.Core` knows about data, and only
`YahooChartHistoryClient` knows about Yahoo's JSON. Swapping data source means
implementing `IFxHistoryProvider`; adding OHLC output means extending `FxRate` and `ExcelValues.ToRateArray`.

## Building (Windows)

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

## Running the add-in

`XlyFinance.AddIn` builds an Excel-DNA add-in into `src\XlyFinance.AddIn\bin\Debug\net10.0-windows\`:

- `XlyFinance64.xll` — unpacked 64-bit add-in, used for debugging (loads the DLLs next to it)
- `XlyFinance64.dna`, `XlyFinance.AddIn.dll`, `XlyFinance.Core.dll` — pieces the unpacked `.xll` needs
- `publish\XlyFinance64-packed.xll` — single-file 64-bit add-in, the thing you would hand to a colleague
- `XlyFinance.xll` / `publish\XlyFinance-packed.xll` — the same for 32-bit Excel

To try it: open Excel → *File → Options → Add-ins → Excel Add-ins → Go… → Browse…* → pick
`publish\XlyFinance64-packed.xll`. Then type a formula.

### F5 debugging

Set `XlyFinance.AddIn` as the startup project and press **F5**. Excel-DNA writes
`Properties\launchSettings.json` on build with an `Excel` profile that runs `EXCEL.EXE /x "XlyFinance64.xll"`,
so the unpacked add-in is loaded for the debug session and breakpoints in `FxFunctions` and `XlyFinance.Core`
are hit.

If Excel starts but `=FXRATES(...)` says `#NAME?`, Excel was launched in a different working directory and never
found the `.xll` (the profile passes a bare file name, and the build task rewrites it on every build). Load the
add-in by hand as above, then use *Debug → Attach to Process… → EXCEL.EXE* — breakpoints work the same.

Either way, **close Excel before rebuilding**: the loaded `.xll` is locked and the build fails with a
file-in-use error.

## How a request flows

```
FXRATES(...)                        Excel-DNA UDF: coerce args, build FxQuery
  ExcelAsyncUtil.RunTask…           async: returns #GETTING_DATA, then the array
  FxHistoryService                  clean up (range filter, sort, dedupe, drop zero closes), map errors
  IFxHistoryProvider                YahooFxHistoryProvider: retries transient failures
  IYahooHistoryClient               YahooChartHistoryClient: v8/finance/chart HTTP call
```

Bad arguments are rejected before any HTTP call (the UDF returns `#VALUE!` and says why). Everything after that
comes back as an `FxError` — `NoData`, `ProviderUnavailable` or `Unexpected` — so the UDF can pick an Excel
error and record the detail for `FXLASTERROR()` and the trace log.

## Known limitations / next steps

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
- `FXRATES` is an async UDF, so it shows `#GETTING_DATA` for a moment and then fills. Excel 365 spills the
  result; in older Excel the formula has to be entered as an array (`Ctrl+Shift+Enter`).

## Troubleshooting

- **Visual Studio refuses `net10.0-windows`** — that TFM needs VS 2022 17.14+ (or VS 2026) and the .NET 10 SDK.
  Either update, or retarget all three projects to `net8.0-windows` / `net8.0` (Excel-DNA 1.9 supports it;
  nothing else changes).
- **Excel says the add-in cannot be loaded** — check bitness (32-bit Excel needs `XlyFinance-packed.xll`, 64-bit
  needs `XlyFinance64-packed.xll`) and that the .NET 10 runtime is installed.
- **`#VALUE!` with no explanation** — run `=FXLASTERROR()` in a cell; it holds the message from the last failure.
- **`#N/A`** — Yahoo returned no rows for that pair/window. Check `=FXSYMBOL("...")` gives a real ticker and that
  the window contains trading days.
- **Nothing refreshes** — identical arguments reuse the cached async value, which is the point (it keeps F9
  cheap). Changing any argument (for example nudging the end date) creates a new topic and a new request;
  closing and reopening the workbook also clears it.

## Development notes

`XlyFinance.Core` and the tests are plain `net10.0`, so they build and run on any OS with the .NET 10 SDK.
Only `XlyFinance.AddIn` is Windows specific (`net10.0-windows`), because Excel-DNA builds the `.xll` there.
Building the add-in off Windows needs `dotnet build -p:EnableWindowsTargeting=true`.

`dotnet test` covers the domain, the retry policy and the JSON mapping; it does not call Yahoo. The live path
was checked by hand against `EURUSD=X` (see the example above), including a weekend-only window and a bogus
ticker.
