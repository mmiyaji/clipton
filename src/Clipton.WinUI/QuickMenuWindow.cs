using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Dispatching;
using Windows.Graphics;
using Windows.System;
using WinRT.Interop;
using Forms = System.Windows.Forms;

namespace Clipton.WinUI;

public sealed class QuickMenuWindow : Window
{
    private const int RowHeight = 34;
    private readonly QuickMenuNavigator _navigator;
    private readonly Border _root = new();
    private readonly ListView _itemsList = new();
    private readonly string _theme;
    private readonly bool _simpleMode;
    private readonly NativeMethods.LowLevelKeyboardProc _keyboardProc;
    private AppWindow? _appWindow;
    private IntPtr _hwnd;
    private IntPtr _keyboardHook;
    private bool _isInvoking;
    private bool _dismissed;
    private Brush _panelBrush = new SolidColorBrush(Colors.White);
    private Brush _borderBrush = new SolidColorBrush(Colors.LightGray);
    private Brush _textBrush = new SolidColorBrush(Colors.Black);
    private Brush _secondaryBrush = new SolidColorBrush(Colors.Gray);
    private Brush _selectionBrush = new SolidColorBrush(Colors.LightGray);
    private Brush _separatorBrush = new SolidColorBrush(Colors.LightGray);

    public QuickMenuWindow(string title, IReadOnlyList<QuickMenuItem> items, string theme, bool simpleMode)
    {
        _navigator = new QuickMenuNavigator(title, items);
        _theme = theme;
        _simpleMode = simpleMode;
        _keyboardProc = OnKeyboardHook;
        Title = "Clipton";
        BuildUi();
        ApplyMenuState();
        PositionNearCursor();
        InstallKeyboardHook();
        Activated += OnActivated;
        Closed += (_, _) => UninstallKeyboardHook();
    }

    public event EventHandler? Dismissed;

    public void Dismiss()
    {
        if (_dismissed)
        {
            return;
        }

        _dismissed = true;
        UninstallKeyboardHook();
        _appWindow?.Hide();
        Dismissed?.Invoke(this, EventArgs.Empty);
    }

    public void FocusMenu()
    {
        Activate();
        if (_hwnd != IntPtr.Zero)
        {
            NativeMethods.SetForegroundWindow(_hwnd);
        }

        _itemsList.Focus(FocusState.Programmatic);
    }

    private void BuildUi()
    {
        var dark = string.Equals(_theme, "dark", StringComparison.OrdinalIgnoreCase) || _simpleMode;
        _panelBrush = Brush(dark ? "#2C2C2C" : "#F9F9F9");
        _borderBrush = Brush(dark ? "#484848" : "#D6D6D6");
        _textBrush = Brush(dark ? "#F3F3F3" : "#1A1A1A");
        _secondaryBrush = Brush(dark ? "#C8C8C8" : "#616161");
        _selectionBrush = Brush(dark ? "#3E3E3E" : "#E9E9E9");
        _separatorBrush = Brush(dark ? "#3A3A3A" : "#E3E3E3");

        _root.Padding = new Thickness(4);
        _root.CornerRadius = new CornerRadius(8);
        _root.Background = _panelBrush;
        _root.BorderBrush = _borderBrush;
        _root.BorderThickness = new Thickness(1);
        _root.Child = _itemsList;
        _root.KeyDown += OnKeyDown;
        _root.IsTabStop = true;
        _itemsList.Background = new SolidColorBrush(Colors.Transparent);
        _itemsList.BorderThickness = new Thickness(0);
        _itemsList.Padding = new Thickness(0);
        _itemsList.SelectionMode = ListViewSelectionMode.Single;
        _itemsList.KeyDown += OnKeyDown;
        _itemsList.SelectionChanged += OnSelectionChanged;
        AddKeyboardAccelerators();
        Content = _root;
    }

    private void AddKeyboardAccelerators()
    {
        AddAccelerator(VirtualKey.Enter, () => InvokeSelected(asPlainText: false));
        AddAccelerator(VirtualKey.T, () => InvokeSelected(asPlainText: true));
        AddAccelerator(VirtualKey.Escape, Dismiss);
        AddAccelerator(VirtualKey.Right, OpenSelectedFolder);
        AddAccelerator(VirtualKey.Left, NavigateBackOrClose);
        AddAccelerator(VirtualKey.Back, NavigateBackOrClose);
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
                DispatchMenuAction(() => MoveSelection(1));
                break;
            case NativeMethods.VkUp:
                DispatchMenuAction(() => MoveSelection(-1));
                break;
            case NativeMethods.VkReturn:
                DispatchMenuAction(() => InvokeSelected(asPlainText: false));
                break;
            case 'T':
                DispatchMenuAction(() => InvokeSelected(asPlainText: true));
                break;
            case NativeMethods.VkEscape:
                DispatchMenuAction(Close);
                break;
            case NativeMethods.VkRight:
                DispatchMenuAction(OpenSelectedFolder);
                break;
            case NativeMethods.VkLeft:
            case NativeMethods.VkBack:
                DispatchMenuAction(NavigateBackOrClose);
                break;
            default:
                handled = false;
                break;
        }

        return handled
            ? 1
            : NativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    private void DispatchMenuAction(Action action)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_isInvoking)
            {
                action();
            }
        });
    }

    private void AddAccelerator(VirtualKey key, Action action)
    {
        var accelerator = new KeyboardAccelerator { Key = key };
        accelerator.Invoked += (_, e) =>
        {
            action();
            e.Handled = true;
        };
        _root.KeyboardAccelerators.Add(accelerator);
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated && !_isInvoking)
        {
            Dismiss();
        }
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Enter:
                InvokeSelected(asPlainText: false);
                e.Handled = true;
                break;
            case VirtualKey.T:
                InvokeSelected(asPlainText: true);
                e.Handled = true;
                break;
            case VirtualKey.Escape:
                Dismiss();
                e.Handled = true;
                break;
            case VirtualKey.Right:
                OpenSelectedFolder();
                e.Handled = true;
                break;
            case VirtualKey.Left:
            case VirtualKey.Back:
                NavigateBackOrClose();
                e.Handled = true;
                break;
        }
    }

    private void NavigateBackOrClose()
    {
        if (_navigator.NavigateBack())
        {
            ApplyMenuState();
        }
        else
        {
            Dismiss();
        }
    }

    private void MoveSelection(int delta)
    {
        if (_navigator.MoveSelection(delta))
        {
            _itemsList.SelectedIndex = _navigator.SelectedIndex;
            FocusMenu();
        }
    }

    private void OpenSelectedFolder()
    {
        if (_navigator.OpenSelectedFolder())
        {
            ApplyMenuState();
            _root.Focus(FocusState.Programmatic);
        }
    }

    private void InvokeSelected(bool asPlainText)
    {
        if (_navigator.SelectedItem is not { IsEnabled: true } item)
        {
            return;
        }

        if (!asPlainText && item.IsFolder)
        {
            OpenSelectedFolder();
            return;
        }

        var action = asPlainText ? item.PlainTextInvoke : item.Invoke;
        if (action is null)
        {
            return;
        }

        _isInvoking = true;
        _appWindow?.Hide();
        _ = Task.Delay(120).ContinueWith(_ => DispatcherQueue.TryEnqueue(() =>
        {
            action();
            Dismiss();
        }));
    }

    private void ApplyMenuState()
    {
        RenderRows();
        ResizeToContent();
        FocusMenu();
    }

    private void RenderRows()
    {
        _itemsList.ItemsSource = _navigator.Items
            .Select((item, index) => CreateRow(item, index))
            .ToArray();
        _itemsList.SelectedIndex = _navigator.SelectedIndex;
        _itemsList.Focus(FocusState.Programmatic);
    }

    private UIElement CreateRow(QuickMenuItem item, int index)
    {
        if (item.IsSeparator)
        {
            return new Border
            {
                Height = 1,
                Margin = new Thickness(12, 5, 12, 5),
                Background = _separatorBrush,
                IsHitTestVisible = false
            };
        }

        var selected = index == _navigator.SelectedIndex;
        var row = new Border
        {
            Height = RowHeight,
            Padding = new Thickness(10, 4, 10, 4),
            CornerRadius = new CornerRadius(4),
            Background = selected ? _selectionBrush : new SolidColorBrush(Colors.Transparent)
        };
        row.PointerEntered += (_, _) =>
        {
            _navigator.Select(index);
            _itemsList.SelectedIndex = _navigator.SelectedIndex;
        };
        row.Tapped += (_, _) =>
        {
            _navigator.Select(index);
            InvokeSelected(asPlainText: false);
        };

        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Child = grid;

        var icon = new TextBlock
        {
            Text = item.IsFolder ? "\uE8B7" : string.Empty,
            FontFamily = new FontFamily("Segoe Fluent Icons"),
            FontSize = 16,
            Foreground = Brush("#60CDFF"),
            VerticalAlignment = VerticalAlignment.Center
        };
        grid.Children.Add(icon);

        var text = new TextBlock
        {
            Text = item.Title,
            Foreground = _textBrush,
            FontSize = 14,
            FontFamily = new FontFamily("Segoe UI Variable Text"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var hint = new TextBlock
        {
            Text = item.IsFolder ? "\uE974" : item.CommandHint,
            Foreground = _secondaryBrush,
            FontSize = item.IsFolder ? 14 : 12,
            FontFamily = item.IsFolder ? new FontFamily("Segoe Fluent Icons") : new FontFamily("Segoe UI Variable Text"),
            Margin = new Thickness(18, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(hint, 2);
        grid.Children.Add(hint);

        return row;
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_itemsList.SelectedIndex >= 0)
        {
            _navigator.Select(_itemsList.SelectedIndex);
        }
    }

    private void PositionNearCursor()
    {
        _hwnd = WindowNative.GetWindowHandle(this);
        var id = Win32Interop.GetWindowIdFromWindow(_hwnd);
        _appWindow = AppWindow.GetFromWindowId(id);
        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }

        var point = Forms.Cursor.Position;
        _appWindow.Move(new PointInt32(point.X, point.Y));
        ResizeToContent();
        NativeMethods.SetForegroundWindow(_hwnd);
    }

    private void ResizeToContent()
    {
        if (_appWindow is null)
        {
            return;
        }

        var rowHeights = _navigator.Items.Sum(item => item.IsSeparator ? 11 : RowHeight);
        var height = Math.Clamp(rowHeights + 10, 48, 560);
        _appWindow.Resize(new SizeInt32(360, height));
    }

    private static SolidColorBrush Brush(string color)
    {
        return new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(
            255,
            Convert.ToByte(color.Substring(1, 2), 16),
            Convert.ToByte(color.Substring(3, 2), 16),
            Convert.ToByte(color.Substring(5, 2), 16)));
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
