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
| `FXLASTERROR()` | The message behind the most recent `FXRATES`/`FXRANGE` failure. Excel errors such as `#VALUE!` carry no detail, this does. |

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
- .NET 10 SDK — Visual Studio 2022 **17.14 or newer**, or **Visual Studio 2026** (Community is fine), with
  the *.NET desktop development* workload

```
git clone <this repo>
cd xlyfinance
dotnet build
```

The build writes a single-file add-in to
`src\XlyFinance.AddIn\bin\Debug\net10.0-windows\publish\XlyFinance64-packed.xll`. Load it: open Excel →
*File → Options → Add-ins → Excel Add-ins → Go… → Browse…* → pick that file. Then type a formula.

The same file is what you hand to a colleague (32-bit Excel needs `XlyFinance-packed.xll` instead).

## Limitations

- Yahoo's `v8/finance/chart` endpoint is **unofficial**: it needs no session but can throttle, and it is under
  no obligation to keep working.
- Daily **closes only**, one pair per call. No OHLCV, no multi-pair matrix.
- Excel's 1900 date system is assumed; the 1904 (old Mac) system is not handled.
- Bars are dated in the exchange's timezone (Europe/London for FX, with historical daylight-saving rules),
  not shifted to your machine's local timezone.
- `FXRATES` is async: it shows `#GETTING_DATA` for a moment and then fills. Excel 365 spills the result; in
  older Excel the formula has to be entered as an array (`Ctrl+Shift+Enter`).
- There is no explicit "refresh now" function — values are cached per argument set (see the notes above).

## Troubleshooting

- **Excel says the add-in cannot be loaded** — check bitness (32-bit Excel needs `XlyFinance-packed.xll`, 64-bit
  needs `XlyFinance64-packed.xll`) and that the .NET 10 runtime is installed.
- **`#VALUE!` with no explanation** — run `=FXLASTERROR()` in a cell; it holds the message from the last failure.
- **`#N/A`** — Yahoo returned no rows for that pair/window. Check `=FXSYMBOL("...")` gives a real ticker and that
  the window contains trading days.
- **Nothing refreshes** — identical arguments reuse the cached async value, which is the point (it keeps F9
  cheap). Changing any argument (for example nudging the end date) creates a new topic and a new request;
  closing and reopening the workbook also clears it.

## Contributing

Building the add-in, F5 debugging, the project layout, how a request flows and the data-source details all
live in [CONTRIBUTING.md](CONTRIBUTING.md).

## Licence

MIT — see [LICENSE](LICENSE). Copyright (c) 2026 Benjamin Allen.

Yahoo Finance data is not covered by this licence; it remains subject to Yahoo's own terms of use.