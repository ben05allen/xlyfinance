# C# & Excel-DNA Project Guidelines

## Tech Stack
- Target Framework: .NET 10.0 (`net10.0-windows`)
- Language: C# 14
- Key Packages: `ExcelDna.Integration`, `YahooFinanceApi`

## Architecture Conventions
- Use async/await patterns where possible, returning `Task<object>` or Excel-DNA `ExcelAsyncUtil`.
- All Excel User Defined Functions (UDFs) must be annotated with `[ExcelFunction]`.
- Enforce clean separation between external API calls (`YahooFinanceApi`) and Excel-DNA wrapper methods.

## Verification Protocol
- Before marking a task complete, run `dotnet build` to verify compilation.
- Run `dotnet test` to execute all unit tests.
