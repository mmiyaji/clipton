using System.Runtime.InteropServices;
using Clipton.WinUI;

namespace Clipton.WinUI.Tests;

public sealed class ClipboardRetryTests
{
    [Fact]
    public void Execute_RetriesTransientFailureAndReturnsSuccessfulResult()
    {
        var attempts = 0;
        var delays = new List<int>();
        var logged = new List<Exception>();

        var result = ClipboardRetry.Execute<int>(
            () => ++attempts == 1 ? throw new COMException("clipboard busy") : 42,
            delays.Add,
            logged.Add);

        Assert.Equal(42, result);
        Assert.Equal(2, attempts);
        Assert.Equal([75], delays);
        Assert.Empty(logged);
    }

    [Fact]
    public void Execute_ReturnsDefaultAndLogsOnceAfterFinalTransientFailure()
    {
        var attempts = 0;
        var delays = new List<int>();
        var logged = new List<Exception>();

        var result = ClipboardRetry.Execute<string>(
            () =>
            {
                attempts++;
                throw new COMException("clipboard remained busy");
            },
            delays.Add,
            logged.Add);

        Assert.Null(result);
        Assert.Equal(20, attempts);
        Assert.Equal(Enumerable.Repeat(75, 19), delays);
        Assert.Single(logged);
        Assert.IsType<COMException>(Assert.Single(logged));
    }

    [Fact]
    public void Execute_DoesNotRetryOrSwallowUnexpectedFailures()
    {
        var attempts = 0;
        var delays = new List<int>();
        var logged = new List<Exception>();

        var exception = Assert.Throws<InvalidOperationException>(() => ClipboardRetry.Execute<int>(
            () =>
            {
                attempts++;
                throw new InvalidOperationException("unexpected");
            },
            delays.Add,
            logged.Add));

        Assert.Equal("unexpected", exception.Message);
        Assert.Equal(1, attempts);
        Assert.Empty(delays);
        Assert.Empty(logged);
    }
}
