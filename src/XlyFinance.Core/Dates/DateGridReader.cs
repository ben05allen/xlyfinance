namespace XlyFinance.Core.Dates;

/// <summary>
/// The first value in a grid that could not be read as a date, with its position.
/// </summary>
/// <param name="Value">The offending cell value, so the caller can describe it.</param>
/// <param name="Row">Zero-based row inside the grid.</param>
/// <param name="Column">Zero-based column inside the grid.</param>
public readonly record struct DateGridError(object? Value, int Row, int Column);

/// <summary>
/// Flattens a two-dimensional block of spreadsheet values (as Excel hands a range to a UDF) into a
/// date list in row-major order. Kept free of Excel types so it can be tested directly.
/// </summary>
public static class DateGridReader
{
    /// <summary>
    /// Reads every cell of <paramref name="cells"/> as a date, left to right, top to bottom.
    /// </summary>
    /// <returns><c>false</c> when any cell is not a date; <paramref name="dates"/> is then empty.</returns>
    public static bool TryRead(object?[,] cells, out DateOnly[] dates, out DateGridError? error)
    {
        ArgumentNullException.ThrowIfNull(cells);

        var rows = cells.GetLength(0);
        var columns = cells.GetLength(1);
        var values = new DateOnly[rows * columns];

        var index = 0;

        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var cell = cells[row, column];

                if (!DateValueParser.TryParse(cell, out var date))
                {
                    dates = [];
                    error = new DateGridError(cell, row, column);
                    return false;
                }

                values[index++] = date;
            }
        }

        dates = values;
        error = null;
        return true;
    }
}
