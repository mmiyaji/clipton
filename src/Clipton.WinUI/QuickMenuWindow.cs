using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.System;
using WinRT.Interop;
using Forms = System.Windows.Forms;

namespace Clipton.WinUI;

public sealed class QuickMenuWindow : Window
{
    private const int MaxMenuTextLength = 56;
    private readonly QuickMenuNavigator _navigator;
    private readonly Grid _host = new();
    private readonly MenuFlyout _flyout = new();
    private readonly string _theme;
    private readonly bool _simpleMode;
    private readonly NativeMethods.LowLevelKeyboardProc _keyboardProc;
    private AppWindow? _appWindow;
    private IntPtr _hwnd;
    private IntPtr _keyboardHook;
    private bool _dismissed;
    private bool _opened;
    private int _lastNavigationKey;
    private long _lastNavigationTick;

    public QuickMenuWindow(string title, IReadOnlyList<QuickMenuItem> items, string theme, bool simpleMode)
    {
        _navigator = new QuickMenuNavigator(title, items);
        _theme = theme;
        _simpleMode = simpleMode;
        _keyboardProc = OnKeyboardHook;
        Title = "Clipton";
        BuildHost();
        BuildFlyout();
        PositionNearCursor();
        _flyout.Closed += (_, _) => Dismiss();
    }

    public event EventHandler? Dismissed;

    public void FocusMenu()
    {
        Activate();
        FocusHostWindow();
        DispatcherQueue.TryEnqueue(() =>
        {
            FocusHostWindow();
            ShowFlyout();
        });
    }

    public void Dismiss()
    {
        if (_dismissed)
        {
            return;
        }

        _dismissed = true;
        UninstallKeyboardHook();
        _flyout.Hide();
        _appWindow?.Hide();
        Dismissed?.Invoke(this, EventArgs.Empty);
    }

    private void BuildHost()
    {
        var dark = string.Equals(_theme, "dark", StringComparison.OrdinalIgnoreCase) || _simpleMode;
        _host.Background = new SolidColorBrush(dark ? Colors.Transparent : Colors.Transparent);
        _host.IsTabStop = true;
        _host.Loaded += (_, _) => ShowFlyout();
        Content = _host;
    }

    private void ShowFlyout()
    {
        if (_opened || _dismissed || _host.XamlRoot is null)
        {
            return;
        }

        _opened = true;
        FocusHostWindow();
        _flyout.ShowAt(_host);
        DispatcherQueue.TryEnqueue(() =>
        {
            FocusHostWindow();
            SelectFirstMenuItem();
            InstallKeyboardHook();
        });
    }

    private void FocusHostWindow()
    {
        if (_hwnd == IntPtr.Zero)
        {
            _host.Focus(FocusState.Programmatic);
            return;
        }

        var foregroundWindow = NativeMethods.GetForegroundWindow();
        var currentThread = NativeMethods.GetCurrentThreadId();
        var foregroundThread = foregroundWindow == IntPtr.Zero
            ? 0
            : NativeMethods.GetWindowThreadProcessId(foregroundWindow, out _);
        var attached = foregroundThread != 0
            && foregroundThread != currentThread
            && NativeMethods.AttachThreadInput(currentThread, foregroundThread, true);

        try
        {
            NativeMethods.BringWindowToTop(_hwnd);
            NativeMethods.SetForegroundWindow(_hwnd);
            NativeMethods.SetActiveWindow(_hwnd);
            NativeMethods.SetFocus(_hwnd);
            _host.Focus(FocusState.Programmatic);
        }
        finally
        {
            if (attached)
            {
                NativeMethods.AttachThreadInput(currentThread, foregroundThread, false);
            }
        }
    }

    private static void SelectFirstMenuItem()
    {
        NativeMethods.keybd_event(NativeMethods.VkDownByte, 0, 0, UIntPtr.Zero);
        NativeMethods.keybd_event(NativeMethods.VkDownByte, 0, NativeMethods.KeyeventfKeyup, UIntPtr.Zero);
    }

    private void InstallKeyboardHook()
    {
        if (_keyboardHook == IntPtr.Zero)
        {
            _keyboardHook = NativeMethods.SetWindowsHookEx(
                NativeMethods.WhKeyboardLl,
                _keyboardProc,
                NativeMethods.GetModuleHandle(null),
                0);
        }
    }

    private void UninstallKeyboardHook()
    {
        if (_keyboardHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
        }
    }

    private IntPtr OnKeyboardHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0 || (wParam.ToInt32() != NativeMethods.WmKeydown && wParam.ToInt32() != NativeMethods.WmSyskeydown))
        {
            return NativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
        }

        var handled = true;
        var key = Marshal.ReadInt32(lParam);
        switch (key)
        {
            case NativeMethods.VkDown:
                if (ShouldHandleNavigationKey(key))
                {
                    _navigator.MoveSelection(1);
                }
                handled = false;
                break;
            case NativeMethods.VkUp:
                if (ShouldHandleNavigationKey(key))
                {
                    _navigator.MoveSelection(-1);
                }
                handled = false;
                break;
            case NativeMethods.VkReturn:
                handled = false;
                break;
            case 'T':
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (_navigator.SelectedItem is { } item)
                    {
                        Invoke(item, asPlainText: true);
                    }
                });
                break;
            case NativeMethods.VkEscape:
                handled = false;
                break;
            default:
                handled = false;
                break;
        }

        return handled
            ? 1
            : NativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    private bool ShouldHandleNavigationKey(int key)
    {
        var now = Environment.TickCount64;
        if (_lastNavigationKey == key && now - _lastNavigationTick < 120)
        {
            return false;
        }

        _lastNavigationKey = key;
        _lastNavigationTick = now;
        return true;
    }

    private void BuildFlyout()
    {
        _flyout.Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft;
        _flyout.Items.Clear();
        AddItems(_flyout.Items, _navigator.Items);
    }

    private void AddItems(IList<MenuFlyoutItemBase> target, IReadOnlyList<QuickMenuItem> items)
    {
        foreach (var item in items)
        {
            if (item.IsSeparator)
            {
                target.Add(new MenuFlyoutSeparator());
                continue;
            }

            if (item.IsFolder)
            {
                var subItem = new MenuFlyoutSubItem
                {
                    Text = TrimForMenu(item.Title),
                    Icon = new FontIcon
                    {
                        Glyph = "\uE8B7",
                        FontFamily = new FontFamily("Segoe Fluent Icons")
                    }
                };
                AddItems(subItem.Items, item.GetChildren());
                target.Add(subItem);
                continue;
            }

            var flyoutItem = new MenuFlyoutItem
            {
                Text = TrimForMenu(item.Title),
                KeyboardAcceleratorTextOverride = item.CommandHint
            };
            flyoutItem.Click += (_, _) => Invoke(item, asPlainText: false);
            target.Add(flyoutItem);

            if (item.PlainTextInvoke is not null)
            {
                var plainTextItem = new MenuFlyoutItem
                {
                    Text = $"{TrimForMenu(item.Title)} (Text)",
                    KeyboardAcceleratorTextOverride = "T",
                    Icon = new FontIcon
                    {
                        Glyph = "\uE8D2",
                        FontFamily = new FontFamily("Segoe Fluent Icons")
                    }
                };
                plainTextItem.Click += (_, _) => Invoke(item, asPlainText: true);
                target.Add(plainTextItem);
            }
        }
    }

    private void Invoke(QuickMenuItem item, bool asPlainText)
    {
        var action = asPlainText ? item.PlainTextInvoke : item.Invoke;
        if (action is null)
        {
            return;
        }

        _flyout.Hide();
        _appWindow?.Hide();
        _ = Task.Delay(90).ContinueWith(_ => DispatcherQueue.TryEnqueue(() =>
        {
            action();
            Dismiss();
        }));
    }

    private void PositionNearCursor()
    {
        _hwnd = WindowNative.GetWindowHandle(this);
        var id = Win32Interop.GetWindowIdFromWindow(_hwnd);
        _appWindow = AppWindow.GetFromWindowId(id);
        MakeHostWindowTransparent();
        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }

        var point = Forms.Cursor.Position;
        _appWindow.Resize(new SizeInt32(8, 8));
        _appWindow.Move(new PointInt32(point.X, point.Y));
        NativeMethods.SetForegroundWindow(_hwnd);
    }

    private static string TrimForMenu(string text)
    {
        var normalized = string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= MaxMenuTextLength
            ? normalized
            : $"{normalized[..(MaxMenuTextLength - 1)]}\u2026";
    }

    private void MakeHostWindowTransparent()
    {
        var exStyle = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GwlExstyle).ToInt64();
        exStyle |= NativeMethods.WsExLayered | NativeMethods.WsExToolwindow;
        exStyle &= ~NativeMethods.WsExAppwindow;
        NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GwlExstyle, new IntPtr(exStyle));
        NativeMethods.SetLayeredWindowAttributes(_hwnd, 0, 255, NativeMethods.LwaColorKey);
    }
}

public sealed record QuickMenuItem(
    string Title,
    string Subtitle,
    string KindLabel,
    string CommandHint,
    Action Invoke,
    Action? PlainTextInvoke = null,
    bool IsEnabled = true,
    IReadOnlyList<QuickMenuItem>? Children = null,
    Func<IReadOnlyList<QuickMenuItem>>? LazyChildren = null,
    bool IsSeparator = false)
{
    private IReadOnlyList<QuickMenuItem>? _resolvedChildren = Children;

    public bool IsSelected { get; set; }

    public bool IsFolder => !IsSeparator && (_resolvedChildren is { Count: > 0 } || LazyChildren is not null);

    public Visibility FolderVisibility => IsFolder ? Visibility.Visible : Visibility.Collapsed;

    public string DisplayHint => IsFolder ? "\uE974" : CommandHint;

    public static QuickMenuItem Separator() => new(string.Empty, string.Empty, string.Empty, string.Empty, () => { }, IsEnabled: false, IsSeparator: true);

    public IReadOnlyList<QuickMenuItem> GetChildren()
    {
        if (_resolvedChildren is not null)
        {
            return _resolvedChildren;
        }

        _resolvedChildren = LazyChildren?.Invoke() ?? [];
        return _resolvedChildren;
    }

    public override string ToString() => Title;
}
