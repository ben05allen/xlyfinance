using Xunit;

namespace XlyFinance.AddIn.Tests;

public class FxRefreshTests
{
    [Fact]
    public void Refresh_advances_the_topic_generation_before_recalculating_once()
    {
        var before = FxFunctions.RefreshGeneration;
        var calls = 0;

        FxFunctions.RefreshRates(() =>
        {
            Assert.NotEqual(before, FxFunctions.RefreshGeneration);
            calls++;
        });

        Assert.Equal(1, calls);
    }
}
