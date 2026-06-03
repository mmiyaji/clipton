using Clipton.Core;
using System.Runtime.InteropServices;

namespace Clipton.WinUI;

public sealed class HotkeyMessageWindow : IDisposable
{
    private readonly ManualResetEventSlim _ready = new();
    private readonly Thread _thread;
    private MessageWindow? _window;
    private bool _disposed;

    public HotkeyMessageWindow(Action<IntPtr> onHotkey, Action onClipboardChanged)
    {
        _thread = new Thread(() =>
        {
            using var window = new MessageWindow(onHotkey, onClipboardChanged);
            _window = window;
            _ready.Set();
            window.RunMessageLoop();
        })
        {
            IsBackground = true,
            Name = "Clipton WinUI hotkey listener"
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait();
    }

    public bool Register(HotkeyGesture gesture)
    {
        if (_disposed || _window is null)
        {
            return false;
        }

        return _window.RequestRegister(gesture);
    }

    public void Unregister()
    {
        if (_disposed || _window is null)
        {
            return;
        }

        _window.RequestUnregister();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _window?.RequestDispose();
        if (!_thread.Join(TimeSpan.FromSeconds(2)))
        {
            _thread.Interrupt();
        }
    }

    private sealed class MessageWindow : IDisposable
    {
        private const int HotkeyId = 0x434C;
        private readonly Action<IntPtr> _onHotkey;
        private readonly Action _onClipboardChanged;
        private readonly object _registrationLock = new();
        private readonly Win32MessageWindow _window;
        private RegistrationRequest? _pendingRegistration;
        private ManualResetEventSlim? _pendingUnregistrationSignal;
        private bool _registered;
        private HotkeyGesture? _registeredGesture;
        private bool _disposed;

        public MessageWindow(Action<IntPtr> onHotkey, Action onClipboardChanged)
        {
            _onHotkey = onHotkey;
            _onClipboardChanged = onClipboardChanged;
            _window = new Win32MessageWindow("CliptonHotkeyMessageWindow", WndProc);
            NativeMethods.AddClipboardFormatListener(Handle);
        }

        private IntPtr Handle => _window.Handle;

        public void RunMessageLoop()
        {
            while (NativeMethods.GetMessage(out var message, IntPtr.Zero, 0, 0))
            {
                NativeMethods.TranslateMessage(ref message);
                NativeMethods.DispatchMessage(ref message);
            }
        }

        public bool RequestRegister(HotkeyGesture gesture)
        {
            var request = new RegistrationRequest(gesture);
            lock (_registrationLock)
            {
                _pendingRegistration = request;
            }

            NativeMethods.PostMessage(Handle, NativeMethods.WmAppRegisterHotkey, IntPtr.Zero, IntPtr.Zero);
            return request.Wait();
        }

        public void RequestDispose()
        {
            NativeMethods.PostMessage(Handle, NativeMethods.WmAppDisposeHotkeyWindow, IntPtr.Zero, IntPtr.Zero);
        }

        public void RequestUnregister()
        {
            var signal = new ManualResetEventSlim();
            lock (_registrationLock)
            {
                _pendingUnregistrationSignal = signal;
            }

            NativeMethods.PostMessage(Handle, NativeMethods.WmAppUnregisterHotkey, IntPtr.Zero, IntPtr.Zero);
            signal.Wait(TimeSpan.FromSeconds(1));
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_registered)
            {
                NativeMethods.UnregisterHotKey(Handle, HotkeyId);
                _registered = false;
            }

            NativeMethods.RemoveClipboardFormatListener(Handle);
            _window.Dispose();
        }

        private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == NativeMethods.WmAppRegisterHotkey)
            {
                RegistrationRequest? request;
                lock (_registrationLock)
                {
                    request = _pendingRegistration;
                    _pendingRegistration = null;
                }

                if (request is not null)
                {
                    request.SetResult(Register(request.Gesture));
                }
                return IntPtr.Zero;
            }

            if (msg == NativeMethods.WmAppDisposeHotkeyWindow)
            {
                Dispose();
                NativeMethods.PostQuitMessage(0);
                return IntPtr.Zero;
            }

            if (msg == NativeMethods.WmAppUnregisterHotkey)
            {
                ManualResetEventSlim? signal;
                lock (_registrationLock)
                {
                    signal = _pendingUnregistrationSignal;
                    _pendingUnregistrationSignal = null;
                }

                UnregisterCurrent();
                signal?.Set();
                return IntPtr.Zero;
            }

            if (msg == NativeMethods.WmHotkey && wParam.ToInt32() == HotkeyId)
            {
                AppDiagnostics.Info("Hotkey", "Hotkey message received.");
                _onHotkey(NativeMethods.GetForegroundWindow());
                return IntPtr.Zero;
            }

            if (msg == NativeMethods.WmClipboardUpdate)
            {
                _onClipboardChanged();
                return IntPtr.Zero;
            }

            return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
        }

        private bool Register(HotkeyGesture gesture)
        {
            var previousGesture = _registeredGesture;
            UnregisterCurrent(clearGesture: false);

            var requestedRegistered = NativeMethods.RegisterHotKey(Handle, HotkeyId, ToNativeModifiers(gesture.Modifiers), ToVirtualKey(gesture.Key));
            AppDiagnostics.Info("Hotkey", requestedRegistered
                ? $"Registered hotkey {gesture}."
                : $"Failed to register hotkey {gesture}. error={Marshal.GetLastWin32Error()}");
            _registered = requestedRegistered;
            _registeredGesture = requestedRegistered ? gesture : null;
            if (!requestedRegistered && previousGesture is not null)
            {
                _registered = NativeMethods.RegisterHotKey(Handle, HotkeyId, ToNativeModifiers(previousGesture.Modifiers), ToVirtualKey(previousGesture.Key));
                _registeredGesture = _registered ? previousGesture : null;
            }

            return requestedRegistered;
        }

        private void UnregisterCurrent(bool clearGesture = true)
        {
            if (_registered)
            {
                NativeMethods.UnregisterHotKey(Handle, HotkeyId);
                _registered = false;
            }

            if (clearGesture)
            {
                _registeredGesture = null;
            }
        }

        private static uint ToNativeModifiers(HotkeyModifiers modifiers)
        {
            uint result = 0;
            if (modifiers.HasFlag(HotkeyModifiers.Alt)) result |= NativeMethods.ModAlt;
            if (modifiers.HasFlag(HotkeyModifiers.Control)) result |= NativeMethods.ModControl;
            if (modifiers.HasFlag(HotkeyModifiers.Shift)) result |= NativeMethods.ModShift;
            if (modifiers.HasFlag(HotkeyModifiers.Windows)) result |= NativeMethods.ModWin;
            return result;
        }

        private static uint ToVirtualKey(string key)
        {
            if (key.Length == 1)
            {
                var c = char.ToUpperInvariant(key[0]);
                if (c is >= 'A' and <= 'Z' or >= '0' and <= '9')
                {
                    return c;
                }
            }

            if (key.StartsWith('F') && int.TryParse(key[1..], out var functionKey) && functionKey is >= 1 and <= 24)
            {
                return (uint)(0x70 + functionKey - 1);
            }

            if (string.Equals(key, "SPACE", StringComparison.OrdinalIgnoreCase))
            {
                return 0x20;
            }

            return NativeMethods.VkV;
        }

        private sealed class RegistrationRequest
        {
            private readonly ManualResetEventSlim _signal = new();
            private bool _success;

            public RegistrationRequest(HotkeyGesture gesture)
            {
                Gesture = gesture;
            }

            public HotkeyGesture Gesture { get; }

            public void SetResult(bool success)
            {
                _success = success;
                _signal.Set();
            }

            public bool Wait()
            {
                return _signal.Wait(TimeSpan.FromSeconds(1)) && _success;
            }
        }
    }
}
