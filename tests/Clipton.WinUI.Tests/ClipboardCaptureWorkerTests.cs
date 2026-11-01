using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Clipton.WinUI;

namespace Clipton.WinUI.Tests;

public sealed class ClipboardCaptureWorkerTests
{
    private const int WmWorkerPumpTest = 0x8000 + 0x5A1;
    private static readonly IntPtr HwndMessage = new(-3);

    [Fact]
    public async Task WorkerPumpsNativeWindowMessagesWhileIdle()
    {
        using var worker = new ClipboardCaptureWorker();
        var delivered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var className = $"Clipton.WorkerTest.{Guid.NewGuid():N}";
        var classNamePointer = Marshal.StringToHGlobalUni(className);
        var instance = NativeMethods.GetModuleHandle(null);
        var registered = false;
        var window = IntPtr.Zero;
        var windowProc = new NativeMethods.WindowProc((hwnd, message, wParam, lParam) =>
        {
            if (message == WmWorkerPumpTest)
            {
                delivered.TrySetResult(true);
                return IntPtr.Zero;
            }

            return NativeMethods.DefWindowProc(hwnd, message, wParam, lParam);
        });

        try
        {
            window = await worker.InvokeAsync(() =>
            {
                var windowClass = new NativeMethods.WindowClassEx
                {
                    Size = (uint)Marshal.SizeOf<NativeMethods.WindowClassEx>(),
                    WndProc = windowProc,
                    Instance = instance,
                    ClassName = classNamePointer
                };
                registered = NativeMethods.RegisterClassEx(ref windowClass) != 0;
                if (!registered)
                {
                    return IntPtr.Zero;
                }

                return NativeMethods.CreateWindowEx(
                    0, className, string.Empty, 0, 0, 0, 0, 0, HwndMessage,
                    IntPtr.Zero, instance, IntPtr.Zero);
            }).WaitAsync(TimeSpan.FromSeconds(3));
            Assert.NotEqual(IntPtr.Zero, window);

            Assert.True(NativeMethods.PostMessage(window, WmWorkerPumpTest, IntPtr.Zero, IntPtr.Zero));
            Assert.True(await delivered.Task.WaitAsync(TimeSpan.FromSeconds(3)));

            var destroyed = await worker.InvokeAsync(() => NativeMethods.DestroyWindow(window));
            Assert.True(destroyed);
            window = IntPtr.Zero;
            Assert.True(NativeMethods.UnregisterClass(className, instance));
            registered = false;
        }
        finally
        {
            if (window != IntPtr.Zero)
            {
                await worker.InvokeAsync(() => NativeMethods.DestroyWindow(window));
            }

            if (registered)
            {
                NativeMethods.UnregisterClass(className, instance);
            }

            Marshal.FreeHGlobal(classNamePointer);
            GC.KeepAlive(windowProc);
        }
    }

    [Fact]
    public async Task DisposeDrainsAlreadyQueuedWorkAndRejectsLaterInvocations()
    {
        using var worker = new ClipboardCaptureWorker();
        using var enteredBlockingAction = new ManualResetEventSlim();
        using var releaseBlockingAction = new ManualResetEventSlim();
        var order = new ConcurrentQueue<int>();
        worker.Post(() =>
        {
            enteredBlockingAction.Set();
            releaseBlockingAction.Wait();
        });
        Assert.True(await Task.Run(() => enteredBlockingAction.Wait(TimeSpan.FromSeconds(3))));

        worker.Post(() => order.Enqueue(1));
        var pendingInvocation = worker.InvokeAsync(() =>
        {
            order.Enqueue(2);
            return true;
        });

        var disposeTask = Task.Run(worker.Dispose);
        try
        {
            // Dispose marks the queue complete before its bounded join returns.
            await disposeTask.WaitAsync(TimeSpan.FromSeconds(3));
        }
        finally
        {
            releaseBlockingAction.Set();
        }

        var invocationResult = await pendingInvocation.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(invocationResult);
        Assert.Equal(new[] { 1, 2 }, order.ToArray());

        var ranAfterDispose = false;
        await worker.InvokeAsync(() =>
        {
            ranAfterDispose = true;
            return true;
        });
        Assert.False(ranAfterDispose);
    }
}
