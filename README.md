# XlyFinance

An Excel add-in (Excel-DNA, .NET 10) that loads daily FX rates from Yahoo Finance.

You give it a pair, a start date and an end date; it pulls the daily closing rates for that window and
spills two columns into the sheet: **date** and **close**. Both ends of the range are inclusive.

```
=FXRATES("EURUSD", "2024-01-01", "2024-01-31")

| 45292 | 1.10418 |   <- date serial, format column A as a date
| 45293 | 1.10189 |
| 45294 | 1.10302 |
| ...   | ...     |
```

## Functions

| Function | Returns |
| --- | --- |
| `FXRATES(pair, start_date, end_date)` | Spilled 2-column array: date serial, close. `#N/A` when Yahoo has no observations in the window, `#VALUE!` when the request is invalid or Yahoo could not be reached. |
| `FXSYMBOL(pair)` | The Yahoo ticker for a pair, e.g. `FXSYMBOL("eur/usd")` → `EURUSD=X`. Handy for sanity checks. |
| `FXLASTERROR()` | The message behind the most recent `FXRATES` failure. Excel errors such as `#VALUE!` carry no detail, this does. |

Notes on the data:

- Bars are **daily closes** as reported by Yahoo Finance. Non-trading days (weekends, market holidays) are
  simply absent — the array is not padded with blanks, so row counts can be shorter than the date range.
- The pair is normalised: `EURUSD`, `eur/usd`, `EUR_USD` and `EURUSD=X` all resolve to `EURUSD=X`.
- Recalculation with identical arguments reuses the value already fetched (Excel-DNA async/RTD caching), so
  pressing F9 does not hammer Yahoo. Change an argument, or use the same arguments in a fresh workbook, to
  force a new request.
- Dates are returned as Excel serial numbers, so charts and date maths work. Format the column as a date
  (`Ctrl+1`) to see `2024-01-01` instead of `45292`.

## Layout

```
src/XlyFinance.Core/        net10.0   domain + Yahoo access, no Excel references
  FxPair, FxQuery           input normalisation and validation
  FxHistoryService          fetch orchestration, cleaning, error mapping
  IFxHistoryProvider        abstraction over the data source
  Providers/Yahoo/          IYahooHistoryClient + v8 chart HTTP client + retry provider
  Dates/, Excel/            loose date parsing, Excel serial conversions
src/XlyFinance.AddIn/       net10.0-windows   Excel-DNA UDFs, argument coercion, async plumbing
tests/XlyFinance.Tests/     net10.0   xUnit tests for everything above the transport
```

The rule of thumb: `XlyFinance.AddIn` knows about Excel, `XlyFinance.Core` knows about data, and only
`YahooChartHistoryClient` knows about Yahoo's JSON. Swapping data source means
implementing `IFxHistoryProvider`; adding OHLC output means extending `FxRate` and `ExcelValues.ToRateArray`.

## Building (Windows)

Requirements:

- Visual Studio 2022 **17.14 or newer** (the first release that can build .NET 10 projects), with the
  *.NET desktop development* workload
- .NET 10 SDK
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

To debug: press **F5** in Visual Studio with `XlyFinance.AddIn` as the startup project — Excel-DNA wires up
`Properties\launchSettings.json` on build, so Visual Studio launches Excel with the add-in loaded and
breakpoints hit.

## How a request flows

```
FXRATES(...)                        Excel-DNA UDF: coerce args, build FxQuery
  ExcelAsyncUtil.RunTask…           async: returns #GETTING_DATA, then the array
  FxHistoryService                  validate + normalise (range filter, sort, dedupe, drop zero closes)
  IFxHistoryProvider                YahooFxHistoryProvider: retries transient failures
  IYahooHistoryClient               YahooChartHistoryClient: v8/finance/chart HTTP call
```

Failures are converted to `FxError` (`InvalidRequest`, `NoData`, `ProviderUnavailable`, `Unexpected`) so the
UDF can pick an Excel error and record the detail for `FXLASTERROR()` and the Excel-DNA diagnostics log.

## Known limitations / next steps

- Yahoo is an unofficial source: the `v8/finance/chart` endpoint needs no session but can throttle, and it is
  under no obligation to keep working. Everything is behind `IFxHistoryProvider` / `IYahooHistoryClient` so a
  fallback (ECB reference rates, another vendor) can be dropped in without touching the UDFs.
- `YahooFinanceApi` 2.3.3 is *not* used: its history method calls `v7/finance/download`, which wants a session
  cookie and crumb and now answers HTTP 401 for every symbol (verified 2024-2025 data, all pairs).
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

- **Visual Studio refuses `net10.0-windows`** — that TFM needs VS 2022 17.14+ and the .NET 10 SDK. Either update,
  or retarget all three projects to `net8.0-windows` / `net8.0` (Excel-DNA 1.9 supports it; nothing else changes).
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
