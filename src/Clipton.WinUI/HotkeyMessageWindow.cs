using Clipton.Core;
using Forms = System.Windows.Forms;

namespace Clipton.WinUI;

public sealed class HotkeyMessageWindow : Forms.NativeWindow, IDisposable
{
    private const int HotkeyId = 0x434C;
    private bool _registered;

    public HotkeyMessageWindow(Action onHotkey, Action onClipboardChanged)
    {
        OnHotkey = onHotkey;
        OnClipboardChanged = onClipboardChanged;
        CreateHandle(new Forms.CreateParams { Caption = "CliptonWinUIMessageWindow" });
        NativeMethods.AddClipboardFormatListener(Handle);
    }

    private Action OnHotkey { get; }

    private Action OnClipboardChanged { get; }

    public void Register(HotkeyGesture gesture)
    {
        if (_registered)
        {
            NativeMethods.UnregisterHotKey(Handle, HotkeyId);
            _registered = false;
        }

        _registered = NativeMethods.RegisterHotKey(Handle, HotkeyId, ToNativeModifiers(gesture.Modifiers), ToVirtualKey(gesture.Key));
    }

    public void Dispose()
    {
        if (_registered)
        {
            NativeMethods.UnregisterHotKey(Handle, HotkeyId);
        }

        NativeMethods.RemoveClipboardFormatListener(Handle);
        DestroyHandle();
    }

    protected override void WndProc(ref Forms.Message m)
    {
        if (m.Msg == NativeMethods.WmHotkey && m.WParam.ToInt32() == HotkeyId)
        {
            OnHotkey();
            return;
        }

        if (m.Msg == NativeMethods.WmClipboardUpdate)
        {
            OnClipboardChanged();
            return;
        }

        base.WndProc(ref m);
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
