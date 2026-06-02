using System.Runtime.InteropServices;

namespace Clipton.WinUI;

internal sealed class Win32MessageWindow : IDisposable
{
    private readonly NativeMethods.WindowProc _windowProc;
    private readonly string _className;
    private readonly Func<IntPtr, uint, IntPtr, IntPtr, IntPtr> _onMessage;
    private readonly IntPtr _classNamePtr;
    private bool _disposed;

    public Win32MessageWindow(string name, Func<IntPtr, uint, IntPtr, IntPtr, IntPtr> onMessage)
    {
        _className = $"{name}-{Guid.NewGuid():N}";
        _onMessage = onMessage;
        _windowProc = WndProc;

        var instance = NativeMethods.GetModuleHandle(null);
        _classNamePtr = Marshal.StringToHGlobalUni(_className);
        var windowClass = new NativeMethods.WindowClassEx
        {
            Size = (uint)Marshal.SizeOf<NativeMethods.WindowClassEx>(),
            WndProc = _windowProc,
            Instance = instance,
            ClassName = _classNamePtr
        };
        if (NativeMethods.RegisterClassEx(ref windowClass) == 0)
        {
            throw new InvalidOperationException($"Failed to register message window class. error={Marshal.GetLastWin32Error()}");
        }

        Handle = NativeMethods.CreateWindowEx(
            0,
            _className,
            name,
            0,
            0,
            0,
            0,
            0,
            IntPtr.Zero,
            IntPtr.Zero,
            instance,
            IntPtr.Zero);
        if (Handle == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Failed to create message window. error={Marshal.GetLastWin32Error()}");
        }
    }

    public IntPtr Handle { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (Handle != IntPtr.Zero)
        {
            NativeMethods.DestroyWindow(Handle);
        }

        if (_classNamePtr != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_classNamePtr);
        }
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        return _onMessage(hWnd, msg, wParam, lParam);
    }
}
