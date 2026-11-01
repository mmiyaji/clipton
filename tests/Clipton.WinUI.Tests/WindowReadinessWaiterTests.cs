using Clipton.WinUI;

namespace Clipton.WinUI.Tests;

public sealed class WindowReadinessWaiterTests
{
    [Fact]
    public async Task WaitAsync_ReturnsWhenWindowIsAlreadyReady()
    {
        await WindowReadinessWaiter.WaitAsync(
            Task.CompletedTask,
            Task.CompletedTask,
            () => true,
            TimeSpan.FromMilliseconds(20));
    }

    [Fact]
    public async Task WaitAsync_ReturnsAfterLoadWhenReadinessConditionIsMet()
    {
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var isReady = false;
        var wait = WindowReadinessWaiter.WaitAsync(loaded.Task, closed.Task, () => isReady, TimeSpan.FromSeconds(1));

        isReady = true;
        loaded.SetResult();
        await wait;
    }

    [Fact]
    public async Task WaitAsync_ReportsWindowClosedBeforeLoad()
    {
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        closed.SetResult();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            WindowReadinessWaiter.WaitAsync(loaded.Task, closed.Task, () => false, TimeSpan.FromSeconds(1)));

        Assert.Contains("closed", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WaitAsync_TimesOutWhenWindowNeverLoads()
    {
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await Assert.ThrowsAsync<TimeoutException>(() =>
            WindowReadinessWaiter.WaitAsync(loaded.Task, closed.Task, () => false, TimeSpan.FromMilliseconds(20)));
    }

    [Fact]
    public async Task WaitAsync_RejectsLoadSignalWithoutReadyRoot()
    {
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        loaded.SetResult();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            WindowReadinessWaiter.WaitAsync(loaded.Task, closed.Task, () => false, TimeSpan.FromSeconds(1)));
    }
}
