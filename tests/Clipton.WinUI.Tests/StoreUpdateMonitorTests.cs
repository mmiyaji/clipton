using Clipton.WinUI;

namespace Clipton.WinUI.Tests;

public sealed class StoreUpdateMonitorTests
{
    [Fact]
    public async Task CheckNowAsync_RaisesAvailabilityChangedOnlyWhenTheAnswerChanges()
    {
        using var monitor = new StoreUpdateMonitor(() => true, _ => Task.FromResult(true));
        var raised = 0;
        monitor.AvailabilityChanged += (_, _) => raised++;

        await monitor.CheckNowAsync();
        await monitor.CheckNowAsync();

        Assert.True(monitor.UpdateAvailable);
        Assert.Equal(1, raised);
        Assert.NotNull(monitor.LastCheckedAt);
    }

    [Fact]
    public async Task CheckNowAsync_DoesNothingWhenAutomaticChecksAreDisabled()
    {
        var calls = 0;
        using var monitor = new StoreUpdateMonitor(() => false, _ =>
        {
            calls++;
            return Task.FromResult(true);
        });

        await monitor.CheckNowAsync();

        Assert.Equal(0, calls);
        Assert.False(monitor.UpdateAvailable);
        Assert.Null(monitor.LastCheckedAt);
    }

    [Fact]
    public async Task CheckNowAsync_KeepsPreviousAnswerWhenTheCheckFails()
    {
        var fail = false;
        using var monitor = new StoreUpdateMonitor(() => true, _ =>
            fail ? throw new InvalidOperationException("offline") : Task.FromResult(true));
        await monitor.CheckNowAsync();
        fail = true;

        var exception = await Record.ExceptionAsync(monitor.CheckNowAsync);

        Assert.Null(exception);
        Assert.True(monitor.UpdateAvailable);
    }

    [Fact]
    public async Task CheckNowAsync_SkipsOverlappingChecks()
    {
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var monitor = new StoreUpdateMonitor(() => true, _ =>
        {
            Interlocked.Increment(ref calls);
            return release.Task;
        });

        var first = monitor.CheckNowAsync();
        await monitor.CheckNowAsync();
        release.SetResult(false);
        await first;

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Start_ChecksAfterTheInitialDelayAndStopsOnDispose()
    {
        var checkedOnce = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var monitor = new StoreUpdateMonitor(
            () => true,
            _ =>
            {
                Interlocked.Increment(ref calls);
                checkedOnce.TrySetResult();
                return Task.FromResult(true);
            },
            initialDelay: TimeSpan.FromMilliseconds(10),
            interval: TimeSpan.FromHours(1));

        monitor.Start();
        monitor.Start();
        await checkedOnce.Task.WaitAsync(TimeSpan.FromSeconds(5));
        monitor.Dispose();
        await Task.Delay(50);

        Assert.Equal(1, calls);
        Assert.True(monitor.UpdateAvailable);
    }

    [Fact]
    public void Report_UpdatesAvailabilityFromTheSettingsWindow()
    {
        using var monitor = new StoreUpdateMonitor(() => true, _ => Task.FromResult(false));
        var raised = 0;
        monitor.AvailabilityChanged += (_, _) => raised++;

        monitor.Report(true);
        monitor.Report(true);
        monitor.Report(false);

        Assert.False(monitor.UpdateAvailable);
        Assert.Equal(2, raised);
    }
}
