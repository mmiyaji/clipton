using Clipton.Core;
using Forms = System.Windows.Forms;

namespace Clipton.WinUI;

public sealed class HotkeyMessageWindow : IDisposable
{
    private readonly ManualResetEventSlim _ready = new();
    private readonly Thread _thread;
    private MessageForm? _form;
    private bool _disposed;

    public HotkeyMessageWindow(Action<IntPtr> onHotkey, Action onClipboardChanged)
    {
        _thread = new Thread(() =>
        {
            using var form = new MessageForm(onHotkey, onClipboardChanged);
            _ = form.Handle;
            _form = form;
            _ready.Set();
            Forms.Application.Run(new Forms.ApplicationContext());
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
        if (_disposed || _form is null)
        {
            return;
        }

        _form.BeginInvoke(() => _form.Register(gesture));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_form is not null && !_form.IsDisposed)
        {
            _form.BeginInvoke(() =>
            {
                _form.Dispose();
                Forms.Application.ExitThread();
            });
        }

        if (!_thread.Join(TimeSpan.FromSeconds(2)))
        {
            _thread.Interrupt();
        }
    }

    private sealed class MessageForm : Forms.Form
    {
        private const int HotkeyId = 0x434C;
        private readonly Action<IntPtr> _onHotkey;
        private readonly Action _onClipboardChanged;
        private bool _registered;

        public MessageForm(Action<IntPtr> onHotkey, Action onClipboardChanged)
        {
            _onHotkey = onHotkey;
            _onClipboardChanged = onClipboardChanged;
            ShowInTaskbar = false;
            FormBorderStyle = Forms.FormBorderStyle.None;
            StartPosition = Forms.FormStartPosition.Manual;
            Location = new System.Drawing.Point(-32000, -32000);
            Size = new System.Drawing.Size(1, 1);
            Opacity = 0;
        }

        protected override void SetVisibleCore(bool value)
        {
            base.SetVisibleCore(false);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            NativeMethods.AddClipboardFormatListener(Handle);
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            if (_registered)
            {
                NativeMethods.UnregisterHotKey(Handle, HotkeyId);
                _registered = false;
            }

            NativeMethods.RemoveClipboardFormatListener(Handle);
            base.OnHandleDestroyed(e);
        }

        public void Register(HotkeyGesture gesture)
        {
            if (_registered)
            {
                NativeMethods.UnregisterHotKey(Handle, HotkeyId);
                _registered = false;
            }

            _registered = NativeMethods.RegisterHotKey(Handle, HotkeyId, ToNativeModifiers(gesture.Modifiers), ToVirtualKey(gesture.Key));
        }

        protected override void WndProc(ref Forms.Message m)
        {
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
