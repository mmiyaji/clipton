using System.Runtime.InteropServices;
using Clipton.Core;
using Forms = System.Windows.Forms;

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
            window.Create();
            _window = window;
            _ready.Set();
            Forms.Application.Run();
        })
        {
            IsBackground = true,
            Name = "Clipton WinUI hotkey listener"
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait();
    }

    public void Register(HotkeyGesture gesture)
    {
        if (_disposed || _window is null)
        {
            return;
        }

        _window.RequestRegister(gesture);
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

    private sealed class MessageWindow : Forms.NativeWindow, IDisposable
    {
        private const int HotkeyId = 0x434C;
        private readonly Action<IntPtr> _onHotkey;
        private readonly Action _onClipboardChanged;
        private readonly object _registrationLock = new();
        private readonly NativeMethods.LowLevelKeyboardProc _keyboardProc;
        private HotkeyGesture? _pendingGesture;
        private ManualResetEventSlim? _pendingRegistrationSignal;
        private HotkeyGesture? _activeGesture;
        private IntPtr _keyboardHook;
        private long _lastHotkeyTick;
        private bool _registered;
        private bool _disposed;

        public MessageWindow(Action<IntPtr> onHotkey, Action onClipboardChanged)
        {
            _onHotkey = onHotkey;
            _onClipboardChanged = onClipboardChanged;
            _keyboardProc = OnKeyboardHook;
        }

        public void Create()
        {
            CreateHandle(new Forms.CreateParams
            {
                Caption = "CliptonHotkeyMessageWindow",
                X = -32000,
                Y = -32000,
                Width = 1,
                Height = 1
            });
            NativeMethods.AddClipboardFormatListener(Handle);
            _keyboardHook = NativeMethods.SetWindowsHookEx(
                NativeMethods.WhKeyboardLl,
                _keyboardProc,
                NativeMethods.GetModuleHandle(null),
                0);
        }

        public void RequestRegister(HotkeyGesture gesture)
        {
            using var signal = new ManualResetEventSlim();
            lock (_registrationLock)
            {
                _pendingGesture = gesture;
                _pendingRegistrationSignal = signal;
            }

            NativeMethods.PostMessage(Handle, NativeMethods.WmAppRegisterHotkey, IntPtr.Zero, IntPtr.Zero);
            signal.Wait(TimeSpan.FromSeconds(1));
        }

        public void RequestDispose()
        {
            NativeMethods.PostMessage(Handle, NativeMethods.WmAppDisposeHotkeyWindow, IntPtr.Zero, IntPtr.Zero);
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

            if (_keyboardHook != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_keyboardHook);
                _keyboardHook = IntPtr.Zero;
            }

            NativeMethods.RemoveClipboardFormatListener(Handle);
            DestroyHandle();
        }

        protected override void WndProc(ref Forms.Message m)
        {
            if (m.Msg == NativeMethods.WmAppRegisterHotkey)
            {
                HotkeyGesture? gesture;
                ManualResetEventSlim? signal;
                lock (_registrationLock)
                {
                    gesture = _pendingGesture;
                    _pendingGesture = null;
                    signal = _pendingRegistrationSignal;
                    _pendingRegistrationSignal = null;
                }

                if (gesture is not null)
                {
                    Register(gesture);
                }

                signal?.Set();
                return;
            }

            if (m.Msg == NativeMethods.WmAppDisposeHotkeyWindow)
            {
                Dispose();
                Forms.Application.ExitThread();
                return;
            }

            if (m.Msg == NativeMethods.WmHotkey && m.WParam.ToInt32() == HotkeyId)
            {
                _onHotkey(NativeMethods.GetForegroundWindow());
                return;
            }

            if (m.Msg == NativeMethods.WmClipboardUpdate)
            {
                _onClipboardChanged();
                return;
            }

            base.WndProc(ref m);
        }

        private void Register(HotkeyGesture gesture)
        {
            if (_registered)
            {
                NativeMethods.UnregisterHotKey(Handle, HotkeyId);
                _registered = false;
            }

            _registered = NativeMethods.RegisterHotKey(Handle, HotkeyId, ToNativeModifiers(gesture.Modifiers), ToVirtualKey(gesture.Key));
            _activeGesture = gesture;
        }

        private IntPtr OnKeyboardHook(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode < 0 || (wParam.ToInt32() != NativeMethods.WmKeydown && wParam.ToInt32() != NativeMethods.WmSyskeydown))
            {
                return NativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
            }

            var gesture = _activeGesture;
            if (gesture is null)
            {
                return NativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
            }

            var key = Marshal.ReadInt32(lParam);
            if (key == ToVirtualKey(gesture.Key) && ModifiersPressed(gesture.Modifiers))
            {
                var now = Environment.TickCount64;
                if (now - _lastHotkeyTick > 250)
                {
                    _lastHotkeyTick = now;
                    _onHotkey(NativeMethods.GetForegroundWindow());
                }

                return 1;
            }

            return NativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
        }

        private static bool ModifiersPressed(HotkeyModifiers modifiers)
        {
            return (!modifiers.HasFlag(HotkeyModifiers.Control) || IsKeyDown(0x11))
                && (!modifiers.HasFlag(HotkeyModifiers.Shift) || IsKeyDown(0x10))
                && (!modifiers.HasFlag(HotkeyModifiers.Alt) || IsKeyDown(0x12))
                && (!modifiers.HasFlag(HotkeyModifiers.Windows) || IsKeyDown(0x5B) || IsKeyDown(0x5C));
        }

        private static bool IsKeyDown(int key)
        {
            return (NativeMethods.GetAsyncKeyState(key) & unchecked((short)0x8000)) != 0;
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

            return NativeMethods.VkV;
        }
    }
}
