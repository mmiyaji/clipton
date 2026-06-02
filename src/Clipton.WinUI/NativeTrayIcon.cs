using System.Runtime.InteropServices;
using Drawing = System.Drawing;

namespace Clipton.WinUI;

internal sealed class NativeTrayIcon : IDisposable
{
    private const uint IconId = 1;
    private const uint CommandHistory = 1001;
    private const uint CommandSettings = 1002;
    private const uint CommandExit = 1003;
    private readonly Win32MessageWindow _window;
    private readonly Action _showHistory;
    private readonly Action _showSettings;
    private readonly Action _exit;
    private Drawing.Icon? _icon;
    private bool _added;
    private bool _disposed;
    private string _historyText;
    private string _settingsText;
    private string _exitText;

    public NativeTrayIcon(Drawing.Icon icon, string historyText, string settingsText, string exitText, Action showHistory, Action showSettings, Action exit)
    {
        _showHistory = showHistory;
        _showSettings = showSettings;
        _exit = exit;
        _historyText = historyText;
        _settingsText = settingsText;
        _exitText = exitText;
        _window = new Win32MessageWindow("CliptonTrayWindow", WndProc);
        UpdateIcon(icon);
    }

    public void UpdateIcon(Drawing.Icon icon)
    {
        var previous = _icon;
        _icon = icon;

        var data = CreateNotifyIconData();
        data.uFlags = NativeMethods.NifMessage | NativeMethods.NifIcon | NativeMethods.NifTip;
        data.hIcon = _icon.Handle;
        data.szTip = "Clipton";
        NativeMethods.ShellNotifyIcon(_added ? NativeMethods.NimModify : NativeMethods.NimAdd, ref data);
        _added = true;
        previous?.Dispose();
    }

    public void UpdateMenuText(string historyText, string settingsText, string exitText)
    {
        _historyText = historyText;
        _settingsText = settingsText;
        _exitText = exitText;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_added)
        {
            var data = CreateNotifyIconData();
            NativeMethods.ShellNotifyIcon(NativeMethods.NimDelete, ref data);
        }

        _icon?.Dispose();
        _window.Dispose();
    }

    private NativeMethods.NotifyIconData CreateNotifyIconData()
    {
        return new NativeMethods.NotifyIconData
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.NotifyIconData>(),
            hWnd = _window.Handle,
            uID = IconId,
            uCallbackMessage = NativeMethods.WmAppTrayCallback,
            szTip = "Clipton"
        };
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == NativeMethods.WmAppTrayCallback)
        {
            var mouseMessage = lParam.ToInt32();
            if (mouseMessage == NativeMethods.WmLbuttonup)
            {
                _showSettings();
                return IntPtr.Zero;
            }

            if (mouseMessage == NativeMethods.WmRbuttonup)
            {
                ShowContextMenu();
                return IntPtr.Zero;
            }
        }

        return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        if (!NativeMethods.GetCursorPos(out var point))
        {
            return;
        }

        var menu = NativeMethods.CreatePopupMenu();
        if (menu == IntPtr.Zero)
        {
            return;
        }

        try
        {
            NativeMethods.AppendMenu(menu, NativeMethods.MfString, CommandHistory, _historyText);
            NativeMethods.AppendMenu(menu, NativeMethods.MfString, CommandSettings, _settingsText);
            NativeMethods.AppendMenu(menu, NativeMethods.MfSeparator, 0, null);
            NativeMethods.AppendMenu(menu, NativeMethods.MfString, CommandExit, _exitText);
            NativeMethods.SetForegroundWindow(_window.Handle);
            var command = NativeMethods.TrackPopupMenu(
                menu,
                NativeMethods.TpmReturnCmd | NativeMethods.TpmRightButton,
                point.X,
                point.Y,
                0,
                _window.Handle,
                IntPtr.Zero);
            switch (command)
            {
                case CommandHistory:
                    _showHistory();
                    break;
                case CommandSettings:
                    _showSettings();
                    break;
                case CommandExit:
                    _exit();
                    break;
            }
        }
        finally
        {
            NativeMethods.DestroyMenu(menu);
        }
    }
}
