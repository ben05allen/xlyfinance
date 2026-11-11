# XlyFinance

An Excel add-in (Excel-DNA, .NET 10) that loads daily FX rates from Yahoo Finance.

Two ways in:

- `FXRATES(pair, start, end)` — a date window; spills **date** and **close** columns, both ends inclusive.
- `FXRANGE(pair, dates)` — a cell or range of dates; spills the close for each, in the same shape.

```
=FXRATES("EURUSD", "2024-01-01", "2024-01-10")

| 45292 | 1.1055831909179688 |   <- date serial, format column A as a date
| 45293 | 1.1038745641708374 |
| 45294 | 1.0941756963729858 |
| ...   | ...                |
```

(Those are real values: eight rows for that window, because 6 and 7 January 2024 were a weekend.)

## Functions

All four are in the *Xly Finance* category of Excel's function wizard.

| Function | Returns |
| --- | --- |
| `FXRATES(pair, start_date, end_date)` | Spilled 2-column array: date serial, close. `#N/A` when Yahoo has no observations in the window, `#VALUE!` when the request is invalid or Yahoo could not be reached. |
| `FXRANGE(pair, dates)` | Close for each date in the given cell/range, laid out like the range (a 4x1 range gives 4x1, a 1x4 row gives 1x4, one cell gives one value). `#N/A` per date with no fixing; the whole call fails with `#VALUE!` if any cell is not a date. |
| `FXSYMBOL(pair)` | The Yahoo ticker for a pair, e.g. `FXSYMBOL("eur/usd")` → `EURUSD=X`. Handy for sanity checks. |
| `FXLASTERROR()` | Automatically updates with the message behind the most recent `FXRATES`/`FXRANGE` failure. Excel errors such as `#VALUE!` carry no detail, this does. |

Notes on the data:

- Bars are **daily closes** as reported by Yahoo Finance. Weekends and most market holidays have no bar at
  all — the array is not padded or forward-filled, so row counts can be shorter than the date range.
- The pair is normalised: `EURUSD`, `eur/usd`, `EUR_USD` and `EURUSD=X` all resolve to `EURUSD=X`.
- Recalculation with identical arguments reuses the value already fetched, so pressing F9 does not hammer
  Yahoo. `FXRATES` and `FXRANGE` also share a small in-memory history cache across open workbooks.
- Dates are returned as Excel serial numbers, so charts and date maths work. Format the column as a date
  (`Ctrl+1`) to see `2024-01-01` instead of `45292`.
- A market holiday that Yahoo still reports a bar for (usually a repeat of the previous close) is kept as
  reported; nothing is inferred or forward-filled.
- One pair per call, and the result is a plain array — no formatting, no table, no formulas written into the
  sheet.

### Looking up a date column

```
A1:A6 (dates)          B1:B6  =FXRANGE("EURUSD", A1:A6)
2024-01-03              1.0941756963729858
2024-01-01              1.1055831909179688
2024-01-06 (Sat)        #N/A
2024-01-02              1.1038745641708374
2024-01-03              1.0941756963729858   <- duplicates repeat, order is yours
2024-12-25              1.0402579307556152   <- Yahoo reports a bar, so it is returned
```

- Order, duplicates and blob shape are preserved; nothing is sorted.
- The whole span from the earliest to the latest date is fetched in **one** request, so a column of
  dates costs the same as a single date. Widely scattered dates (years apart) still download the whole
  span in between — use `FXRATES` for a dense window instead.
- `#N/A` marks a date with no fixing. `IFNA`, `ISNA` and charts treat those as gaps.
- A blank cell, a header text, a cell error or a number that is not a valid date fails the entire call
  with `#VALUE!` and `FXLASTERROR()` naming the offending row/column. Select exactly the populated cells.

## Getting started

Requirements:

- Excel (64-bit recommended; the build also emits a 32-bit `.xll`)
- .NET 10 SDK; for IDE builds and debugging, Visual Studio 2026 (Community is fine) with the
  *.NET desktop development* workload

```
git clone <this repo>
cd xlyfinance
dotnet build
```

The build writes a single-file add-in to
`src\XlyFinance.AddIn\bin\Debug\net10.0-windows\publish\XlyFinance64-packed.xll`. Load it: open Excel →
*File → Options → Add-ins → Excel Add-ins → Go… → Browse…* → pick that file. Then type a formula.

The same file is what you hand to a colleague (32-bit Excel needs `XlyFinance-packed.xll` instead).

## Caching and refreshing

Use **Add-ins → Xly Finance → Refresh FX rates** to fetch fresh history for FX formulas in all open
workbooks. This clears the shared history cache and recalculates the workbooks, including formulas that
previously returned errors. Formula arguments stay the same. The command is also available as the
`FXREFRESH` macro, which can be assigned to a button; it is not a worksheet function.

The cache lives only in memory and is limited to 64 date windows and 100,000 observations. A cached window
can serve smaller requests for the same pair, including dates with no bar. Simultaneous requests share a
fetch when an in-progress window covers them. Failures are not stored in the history cache.

Historical windows expire after 24 hours; windows containing today (UTC) expire after five minutes. Expiry
controls reuse by new requests. Existing worksheet results stay stable until **Refresh FX rates** is used;
there is no background polling. Restarting Excel clears the cache too. Nothing is written to disk.

## Limitations

- Yahoo's `v8/finance/chart` endpoint is **unofficial**: it needs no session but can throttle, and it is under
  no obligation to keep working.
- Daily **closes only**, one pair per call. No OHLCV, no multi-pair matrix.
- History requests support dates from **1970-01-01 through today (UTC)**. Earlier and future dates return
  `#VALUE!`; actual history availability depends on Yahoo and the pair.
- Excel's 1900 date system is assumed; the 1904 (old Mac) system is not handled.
- Bars are dated in the exchange's timezone (Europe/London for FX, with historical daylight-saving rules),
  not shifted to your machine's local timezone. If the exchange timezone is missing or cannot be resolved,
  the request fails with a diagnostic rather than guessing dates from a fixed offset.
- `FXRATES` is async: it shows `#GETTING_DATA` for a moment and then fills. Excel 365 spills the result; in
  older Excel the formula has to be entered as an array (`Ctrl+Shift+Enter`).
- Refresh applies to all open workbooks in the current Excel instance and performs a full recalculation.

## Troubleshooting

- **Excel says the add-in cannot be loaded** — check bitness (32-bit Excel needs `XlyFinance-packed.xll`, 64-bit
  needs `XlyFinance64-packed.xll`) and that the .NET 10 runtime is installed.
- **`#VALUE!` with no explanation** — run `=FXLASTERROR()` in a cell; it holds the message from the last failure.
- **`#N/A`** — Yahoo returned no rows for that pair/window. Check `=FXSYMBOL("...")` gives a real ticker and that
  the window contains trading days.
- **Nothing refreshes** — use **Add-ins → Xly Finance → Refresh FX rates**. F9 reuses existing async
  results; editing an argument may still reuse a covering window from the history cache.

## Contributing

Building the add-in, F5 debugging, the project layout, how a request flows and the data-source details all
live in [CONTRIBUTING.md](CONTRIBUTING.md).

## Licence

MIT — see [LICENSE](LICENSE). Copyright (c) 2026 Benjamin Allen.

Yahoo Finance data is not covered by this licence; it remains subject to Yahoo's own terms of use.
