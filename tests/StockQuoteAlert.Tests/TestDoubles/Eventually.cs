namespace StockQuoteAlert.Tests.TestDoubles;

/// <summary>Waits for work running in the background (e.g. a hosted service) to reach a state.</summary>
internal static class Eventually
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    public static async Task HoldsAsync(Func<bool> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);

        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail("The condition did not hold in time.");
            }

            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }
}