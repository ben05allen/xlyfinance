using XlyFinance.Core.Dates;

namespace XlyFinance.Tests;

public class DateGridReaderTests
{
    [Fact]
    public void Reads_cells_left_to_right_top_to_bottom()
    {
        var cells = new object?[,]
        {
            { 45293d, new DateTime(2024, 1, 3) },
            { "2024-01-04", new DateOnly(2024, 1, 5) },
        };

        Assert.True(DateGridReader.TryRead(cells, out var dates, out var error));
        Assert.Null(error);
        Assert.Equal(
            new[] { new DateOnly(2024, 1, 2), new DateOnly(2024, 1, 3), new DateOnly(2024, 1, 4), new DateOnly(2024, 1, 5) },
            dates);
    }

    [Fact]
    public void Reads_a_single_cell_block()
    {
        var cells = new object?[,] { { "2024-01-02 23:59:00" } };

        Assert.True(DateGridReader.TryRead(cells, out var dates, out _));
        Assert.Equal(new[] { new DateOnly(2024, 1, 2) }, dates);
    }

    [Fact]
    public void Reads_an_empty_block_as_no_dates()
    {
        Assert.True(DateGridReader.TryRead(new object?[0, 0], out var dates, out var error));
        Assert.Empty(dates);
        Assert.Null(error);
    }

    [Fact]
    public void Reports_the_first_unreadable_cell()
    {
        var cells = new object?[,]
        {
            { "2024-01-02", "not a date" },
            { 0d, "2024-01-05" },
        };

        Assert.False(DateGridReader.TryRead(cells, out var dates, out var error));
        Assert.Empty(dates);
        Assert.NotNull(error);
        Assert.Equal(0, error!.Value.Row);
        Assert.Equal(1, error.Value.Column);
        Assert.Equal("not a date", error.Value.Value);
    }

    [Fact]
    public void Rejects_blank_cells()
    {
        var cells = new object?[,]
        {
            { "2024-01-02" },
            { null },
        };

        Assert.False(DateGridReader.TryRead(cells, out _, out var error));
        Assert.NotNull(error);
        Assert.Equal(1, error!.Value.Row);
        Assert.Null(error.Value.Value);
    }
}
