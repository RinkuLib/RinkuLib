using Xunit;

namespace RinkuLib.Tests.Execution;

/// <summary>
/// <see cref="TypeAccessing.EnumHelper"/> bridges synchronous sequences into async streams.
/// </summary>
public class EnumHelperTests {
    [Fact]
    public async Task ToAsyncEnumerable_yields_every_item_in_order() {
        var items = new List<int>();
        await foreach (var i in TypeAccessing.EnumHelper.ToAsyncEnumerable([1, 2, 3], TestContext.Current.CancellationToken))
            items.Add(i);
        Assert.Equal([1, 2, 3], items);
    }

    [Fact]
    public async Task ToAsyncEnumerable_stops_on_cancellation_between_items() {
        using var cts = new CancellationTokenSource();
        var seen = new List<int>();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => {
            await foreach (var i in TypeAccessing.EnumHelper.ToAsyncEnumerable([1, 2, 3], cts.Token)) {
                seen.Add(i);
                cts.Cancel();
            }
        });
        Assert.Equal([1], seen);
    }
}
