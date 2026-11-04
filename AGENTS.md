# C# & Excel-DNA Project Guidelines

## Tech Stack
- Target Framework: .NET 10.0 (`net10.0-windows`)
- Language: C# 14
- Key Packages: `ExcelDna.Integration`
  - Yahoo FX data is read from the public `v8/finance/chart` endpoint over `HttpClient` + `System.Text.Json`.
  - `YahooFinanceApi` is deliberately not used: its history call goes to `v7/finance/download`, which now
    answers HTTP 401 for every symbol.

## Architecture Conventions
- Use async/await patterns where possible, returning `Task<object>` or Excel-DNA `ExcelAsyncUtil`.
- All Excel User Defined Functions (UDFs) must be annotated with `[ExcelFunction]`.
- Enforce clean separation between external API calls (Yahoo HTTP, behind `IYahooHistoryClient`) and Excel-DNA wrapper methods.

## Verification Protocol
- Before marking a task complete, run `dotnet build` to verify compilation.
- Run `dotnet test` to execute all unit tests.
