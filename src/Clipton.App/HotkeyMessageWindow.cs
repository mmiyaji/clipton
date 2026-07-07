using System.Windows.Interop;
using System.Windows.Input;
using Clipton.Core;

namespace Clipton.App;

public sealed class HotkeyMessageWindow : IDisposable
{
    private const int HotkeyId = 0x434C;
    private readonly HwndSource _source;
    private HotkeyGesture _gesture = HotkeyGesture.Default;
    private bool _registered;

    public HotkeyMessageWindow(Action onHotkey, Action onClipboardChanged)
    {
        OnHotkey = onHotkey;
        OnClipboardChanged = onClipboardChanged;

        var parameters = new HwndSourceParameters("CliptonMessageWindow")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
        NativeMethods.AddClipboardFormatListener(_source.Handle);
    }

    private Action OnHotkey { get; }

    private Action OnClipboardChanged { get; }

    public bool Register(HotkeyGesture gesture)
    {
        if (_registered)
        {
            NativeMethods.UnregisterHotKey(_source.Handle, HotkeyId);
            _registered = false;
        }

        _gesture = gesture;
        _registered = NativeMethods.RegisterHotKey(_source.Handle, HotkeyId, ToNativeModifiers(gesture.Modifiers), ToVirtualKey(gesture.Key));
        return _registered;
    }

    public void Dispose()
    {
        if (_registered)
        {
            NativeMethods.UnregisterHotKey(_source.Handle, HotkeyId);
        }

        NativeMethods.RemoveClipboardFormatListener(_source.Handle);
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            OnHotkey();
            handled = true;
        }
        else if (msg == NativeMethods.WmClipboardUpdate)
        {
            OnClipboardChanged();
            handled = true;
        }

        return IntPtr.Zero;
    }

    private static uint ToNativeModifiers(HotkeyModifiers modifiers)
    {
        uint result = 0;
        if (modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            result |= NativeMethods.ModAlt;
        }

        if (modifiers.HasFlag(HotkeyModifiers.Control))
        {
            result |= NativeMethods.ModControl;
        }

        if (modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            result |= NativeMethods.ModShift;
        }

        if (modifiers.HasFlag(HotkeyModifiers.Windows))
        {
            result |= NativeMethods.ModWin;
        }

        return result;
    }

    private static uint ToVirtualKey(string key)
    {
        var converted = new KeyConverter().ConvertFromInvariantString(key);
        return converted is Key wpfKey ? (uint)KeyInterop.VirtualKeyFromKey(wpfKey) : (uint)KeyInterop.VirtualKeyFromKey(Key.V);
    }
}
