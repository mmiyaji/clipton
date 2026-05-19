using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using Windows.System;
using WinRT.Interop;
using Forms = System.Windows.Forms;

namespace Clipton.WinUI;

public sealed class QuickMenuWindow : Window
{
    private const int MaxMenuLineLength = 34;
    private readonly QuickMenuNavigator _navigator;
    private readonly Grid _host = new();
    private readonly MenuFlyout _flyout = new();
    private readonly string _theme;
    private readonly bool _simpleMode;
    private readonly NativeMethods.LowLevelKeyboardProc _keyboardProc;
    private readonly List<MenuFlyoutItemBase> _rootFocusableItems = [];
    private readonly Dictionary<MenuFlyoutSubItem, List<MenuFlyoutItemBase>> _childFocusableItems = [];
    private readonly Dictionary<MenuFlyoutItemBase, MenuFlyoutSubItem> _parentItem = [];
    private IReadOnlyList<MenuFlyoutItemBase> _activeFocusableItems = [];
    private MenuFlyoutSubItem? _activeParent;
    private int _focusedIndex = -1;
    private AppWindow? _appWindow;
    private IntPtr _hwnd;
    private IntPtr _keyboardHook;
    private bool _dismissed;
    private bool _opened;
    private int _lastNavigationKey;
    private long _lastNavigationTick;
    private long _focusToken;

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
        DispatcherQueue.TryEnqueue(() =>
        {
            _flyout.Hide();
            _appWindow?.Hide();
            Dismissed?.Invoke(this, EventArgs.Empty);
        });
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
        InstallKeyboardHook();
        FocusHostWindow();
        _flyout.ShowAt(_host);
        var focusToken = ++_focusToken;
        DispatcherQueue.TryEnqueue(() =>
        {
            _ = Task.Delay(180).ContinueWith(_ => DispatcherQueue.TryEnqueue(() =>
            {
                if (_dismissed || _focusToken != focusToken)
                {
                    return;
                }

                FocusHostWindow();
                FocusMenuItem(0);
            }));
        });
    }

    private void FocusHostWindow()
    {
        if (_hwnd != IntPtr.Zero)
        {
            NativeMethods.SetForegroundWindow(_hwnd);
        }

        _host.Focus(FocusState.Programmatic);
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
                    DispatcherQueue.TryEnqueue(() => MoveFocus(1));
                }
                break;
            case NativeMethods.VkUp:
                if (ShouldHandleNavigationKey(key))
                {
                    DispatcherQueue.TryEnqueue(() => MoveFocus(-1));
                }
                break;
            case NativeMethods.VkLeft:
                DispatcherQueue.TryEnqueue(async () =>
                {
                    await Task.Delay(80);
                    ReturnToParentFocusContext();
                });
                handled = false;
                break;
            case NativeMethods.VkRight:
                DispatcherQueue.TryEnqueue(async () =>
                {
                    await Task.Delay(80);
                    EnterChildFocusContext();
                });
                handled = false;
                break;
            case NativeMethods.VkReturn:
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (GetFocusedMenuItem() is { } item)
                    {
                        Invoke(item, asPlainText: false);
                    }
                });
                break;
            case 'T':
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (GetFocusedMenuItem() is { } item)
                    {
                        Invoke(item, asPlainText: true);
                    }
                });
                break;
            case NativeMethods.VkEscape:
                Dismiss();
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
        _rootFocusableItems.Clear();
        _childFocusableItems.Clear();
        _parentItem.Clear();
        _activeFocusableItems = _rootFocusableItems;
        _activeParent = null;
        _focusedIndex = -1;
        AddItems(_flyout.Items, _navigator.Items, parent: null);
    }

    private void AddItems(IList<MenuFlyoutItemBase> target, IReadOnlyList<QuickMenuItem> items, MenuFlyoutSubItem? parent)
    {
        var focusableItems = parent is null
            ? _rootFocusableItems
            : _childFocusableItems.GetValueOrDefault(parent);
        if (focusableItems is null)
        {
            focusableItems = [];
            _childFocusableItems[parent!] = focusableItems;
        }

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
                    Text = FormatMenuText(item.Title),
                    Icon = new FontIcon
                    {
                        Glyph = "\uE8B7",
                        FontFamily = new FontFamily("Segoe Fluent Icons")
                    }
                };
                focusableItems.Add(subItem);
                subItem.Tag = item;
                if (parent is not null)
                {
                    _parentItem[subItem] = parent;
                }

                _childFocusableItems[subItem] = [];
                AddItems(subItem.Items, item.GetChildren(), subItem);
                target.Add(subItem);
                continue;
            }

            var flyoutItem = new MenuFlyoutItem
            {
                Text = FormatMenuText(item.Title),
                KeyboardAcceleratorTextOverride = item.CommandHint,
                Icon = CreateIcon(item)
            };
            focusableItems.Add(flyoutItem);
            if (parent is not null)
            {
                _parentItem[flyoutItem] = parent;
            }

            flyoutItem.Tag = item;
            flyoutItem.Click += (_, _) => Invoke(item, asPlainText: false);
            target.Add(flyoutItem);
        }
    }

    private QuickMenuItem? GetFocusedMenuItem()
    {
        if (_host.XamlRoot is null)
        {
            return _navigator.SelectedItem;
        }

        return FocusManager.GetFocusedElement(_host.XamlRoot) switch
        {
            MenuFlyoutItem { Tag: QuickMenuItem item } => item,
            MenuFlyoutSubItem { Tag: QuickMenuItem item } => item,
            _ => _navigator.SelectedItem
        };
    }

    private void FocusMenuItem(int index)
    {
        if (_activeFocusableItems.Count == 0)
        {
            return;
        }

        _focusedIndex = (index + _activeFocusableItems.Count) % _activeFocusableItems.Count;
        _activeFocusableItems[_focusedIndex].Focus(FocusState.Keyboard);
    }

    private void MoveFocus(int delta)
    {
        SyncFocusedIndex();
        FocusMenuItem(_focusedIndex + delta);
    }

    private void SyncFocusedIndex()
    {
        if (_host.XamlRoot is null)
        {
            return;
        }

        var focusedElement = FocusManager.GetFocusedElement(_host.XamlRoot);
        if (focusedElement is MenuFlyoutItemBase focusedItem
            && _parentItem.TryGetValue(focusedItem, out var parent)
            && _childFocusableItems.TryGetValue(parent, out var childItems))
        {
            _activeFocusableItems = childItems;
            _activeParent = parent;
        }
        else if (focusedElement is MenuFlyoutItemBase rootItem && _rootFocusableItems.Contains(rootItem))
        {
            _activeFocusableItems = _rootFocusableItems;
            _activeParent = null;
        }

        for (var i = 0; i < _activeFocusableItems.Count; i++)
        {
            if (ReferenceEquals(_activeFocusableItems[i], focusedElement))
            {
                _focusedIndex = i;
                return;
            }
        }
    }

    private void EnterChildFocusContext()
    {
        if (_host.XamlRoot is null)
        {
            return;
        }

        if (FocusManager.GetFocusedElement(_host.XamlRoot) is not MenuFlyoutSubItem focusedFolder
            || !_childFocusableItems.TryGetValue(focusedFolder, out var childItems)
            || childItems.Count == 0)
        {
            SyncFocusedIndex();
            return;
        }

        _activeFocusableItems = childItems;
        _activeParent = focusedFolder;
        SyncFocusedIndex();
        if (_focusedIndex < 0 || _focusedIndex >= _activeFocusableItems.Count)
        {
            FocusMenuItem(0);
        }
    }

    private void ReturnToParentFocusContext()
    {
        if (_activeParent is null)
        {
            SyncFocusedIndex();
            return;
        }

        var parent = _activeParent;
        if (_parentItem.TryGetValue(parent, out var grandParent))
        {
            _activeFocusableItems = _childFocusableItems[grandParent];
            _activeParent = grandParent;
        }
        else
        {
            _activeFocusableItems = _rootFocusableItems;
            _activeParent = null;
        }

        FocusMenuItem(IndexOf(_activeFocusableItems, parent));
    }

    private static int IndexOf(IReadOnlyList<MenuFlyoutItemBase> items, MenuFlyoutItemBase item)
    {
        for (var i = 0; i < items.Count; i++)
        {
            if (ReferenceEquals(items[i], item))
            {
                return i;
            }
        }

        return 0;
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

    private static string FormatMenuText(string text)
    {
        var normalized = string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length <= MaxMenuLineLength)
        {
            return normalized;
        }

        var line1 = normalized[..MaxMenuLineLength].TrimEnd();
        var remaining = normalized[MaxMenuLineLength..].TrimStart();
        var line2 = remaining.Length <= MaxMenuLineLength
            ? remaining
            : $"{remaining[..(MaxMenuLineLength - 1)].TrimEnd()}\u2026";

        return $"{line1}{Environment.NewLine}{line2}";
    }

    private static IconElement? CreateIcon(QuickMenuItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.IconImagePath) && File.Exists(item.IconImagePath))
        {
            return new ImageIcon
            {
                Source = new BitmapImage(new Uri(item.IconImagePath))
            };
        }

        if (string.IsNullOrWhiteSpace(item.IconGlyph))
        {
            return null;
        }

        return new FontIcon
        {
            Glyph = item.IconGlyph,
            FontFamily = new FontFamily(item.IconFontFamily ?? "Segoe UI"),
            FontSize = 12
        };
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
    bool IsSeparator = false,
    string? IconGlyph = null,
    string? IconFontFamily = null,
    string? IconImagePath = null)
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
