using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.System;
using WinRT.Interop;
using Forms = System.Windows.Forms;

namespace Clipton.WinUI;

public sealed class QuickMenuWindow : Window
{
    private readonly QuickMenuNavigator _navigator;
    private readonly ListView _itemsList = new();
    private readonly TextBlock _titleText = new() { FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
    private readonly bool _simpleMode;
    private readonly string _theme;
    private readonly QuickMenuWindow? _parentMenu;
    private readonly Action? _onInvoked;
    private readonly PointInt32? _initialPosition;
    private readonly int _windowWidth;
    private readonly int _windowHeight;
    private Brush _panelBrush = new SolidColorBrush(Colors.White);
    private Brush _textBrush = new SolidColorBrush(Colors.Black);
    private Brush _secondaryBrush = new SolidColorBrush(Colors.Gray);
    private Brush _selectionBrush = new SolidColorBrush(Colors.LightGray);
    private Brush _separatorBrush = new SolidColorBrush(Colors.LightGray);
    private Brush _borderBrush = new SolidColorBrush(Colors.LightGray);
    private QuickMenuWindow? _childMenuWindow;
    private AppWindow? _appWindow;
    private bool _isClosing;
    private bool _refreshingSelection;

    public QuickMenuWindow(
        string title,
        IReadOnlyList<QuickMenuItem> items,
        string theme,
        bool simpleMode,
        QuickMenuWindow? parentMenu = null,
        PointInt32? initialPosition = null,
        Action? onInvoked = null)
    {
        _navigator = new QuickMenuNavigator(title, items);
        _simpleMode = simpleMode;
        _theme = theme;
        _parentMenu = parentMenu;
        _initialPosition = initialPosition;
        _onInvoked = onInvoked;
        _windowWidth = _simpleMode ? 316 : 386;
        _windowHeight = CalculateWindowHeight(items, _simpleMode);
        Title = "Clipton";
        BuildUi(theme);
        ApplyMenuState();
        PositionNearCursor();
        Activated += (_, _) => _itemsList.Focus(FocusState.Programmatic);
    }

    public void FocusMenu()
    {
        Activate();
        _itemsList.Focus(FocusState.Programmatic);
    }

    private void BuildUi(string theme)
    {
        var dark = string.Equals(theme, "dark", StringComparison.OrdinalIgnoreCase) || _simpleMode;
        _panelBrush = Brush(dark ? "#2C2C2C" : "#F9F9F9");
        _textBrush = Brush(dark ? "#F3F3F3" : "#1A1A1A");
        _secondaryBrush = Brush(dark ? "#C8C8C8" : "#616161");
        _selectionBrush = Brush(dark ? "#3E3E3E" : "#E9E9E9");
        _separatorBrush = Brush(dark ? "#3A3A3A" : "#E3E3E3");
        _borderBrush = Brush(dark ? "#484848" : "#D6D6D6");

        var root = new Border
        {
            Padding = new Thickness(4),
            CornerRadius = new CornerRadius(8),
            Background = _panelBrush,
            BorderBrush = _borderBrush,
            BorderThickness = new Thickness(1)
        };
        var stack = new StackPanel { Spacing = 0 };
        root.Child = stack;
        Content = root;

        if (!_simpleMode)
        {
            var header = new Grid { Margin = new Thickness(8, 7, 8, 6) };
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _titleText.Foreground = _textBrush;
            header.Children.Add(_titleText);
            var hint = new TextBlock
            {
                Text = "Enter paste  T text  Left/Right",
                FontSize = 11,
                Foreground = _secondaryBrush
            };
            Grid.SetColumn(hint, 1);
            header.Children.Add(hint);
            stack.Children.Add(header);
        }

        _itemsList.Background = new SolidColorBrush(Colors.Transparent);
        _itemsList.BorderThickness = new Thickness(0);
        _itemsList.SelectionMode = ListViewSelectionMode.Single;
        _itemsList.Padding = new Thickness(0);
        _itemsList.MaxHeight = _simpleMode ? 560 : 500;
        _itemsList.MinHeight = 36;
        _itemsList.KeyDown += ItemsList_OnKeyDown;
        _itemsList.SelectionChanged += ItemsList_OnSelectionChanged;
        _itemsList.DoubleTapped += (_, _) => InvokeSelected(asPlainText: false);
        stack.Children.Add(_itemsList);
    }

    private UIElement CreateRowElement(QuickMenuItem item, bool isSelected)
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

        var row = new Border
        {
            MinHeight = 32,
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0),
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Colors.Transparent),
            Background = isSelected ? _selectionBrush : Brush("#00000000")
        };

        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(item.IsFolder ? 20 : 76) });
        row.Child = grid;

        var icon = new TextBlock
        {
            Text = "\uE8B7",
            FontFamily = new FontFamily("Segoe Fluent Icons"),
            FontSize = 16,
            Foreground = Brush("#60CDFF"),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = item.FolderVisibility
        };
        grid.Children.Add(icon);

        var title = new TextBlock
        {
            Text = item.Title,
            Foreground = _textBrush,
            FontSize = 14,
            FontFamily = new FontFamily("Segoe UI Variable Text"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(title, 1);
        grid.Children.Add(title);

        var command = new TextBlock
        {
            Text = item.DisplayHint,
            Foreground = _secondaryBrush,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = item.IsFolder ? 14 : 12,
            FontFamily = item.IsFolder ? new FontFamily("Segoe Fluent Icons") : new FontFamily("Segoe UI Variable Text")
        };
        Grid.SetColumn(command, 2);
        grid.Children.Add(command);
        return row;
    }

    private void ItemsList_OnKeyDown(object sender, KeyRoutedEventArgs e)
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
                if (!NavigateBack()) CloseMenu();
                e.Handled = true;
                break;
            case VirtualKey.Back:
            case VirtualKey.Left:
                NavigateBack();
                e.Handled = true;
                break;
            case VirtualKey.Right:
                OpenSelectedFolder();
                e.Handled = true;
                break;
            case VirtualKey.Up:
                MoveSelection(-1);
                e.Handled = true;
                break;
            case VirtualKey.Down:
                MoveSelection(1);
                e.Handled = true;
                break;
        }
    }

    private void InvokeSelected(bool asPlainText)
    {
        _navigator.Select(_itemsList.SelectedIndex);
        if (_navigator.SelectedItem is not { IsEnabled: true } item)
        {
            return;
        }

        if (!asPlainText && _navigator.OpenSelectedFolder())
        {
            if (_simpleMode)
            {
                _navigator.NavigateBack();
                ShowChildMenu(item);
                return;
            }

            ApplyMenuState();
            return;
        }

        var action = asPlainText ? item.PlainTextInvoke : item.Invoke;
        if (action is null)
        {
            return;
        }

        InvokeAfterClose(action);
    }

    private void MoveSelection(int delta)
    {
        if (_navigator.MoveSelection(delta))
        {
            ApplySelection();
        }
    }

    private void OpenSelectedFolder()
    {
        _navigator.Select(_itemsList.SelectedIndex);
        if (_simpleMode && _navigator.SelectedItem is { IsFolder: true } item)
        {
            ShowChildMenu(item);
            return;
        }

        if (_navigator.OpenSelectedFolder())
        {
            ApplyMenuState();
        }
    }

    private bool NavigateBack()
    {
        if (!_navigator.NavigateBack())
        {
            if (_parentMenu is not null)
            {
                CloseMenu();
                _parentMenu.FocusMenu();
                return true;
            }

            return false;
        }

        ApplyMenuState();
        return true;
    }

    private void ApplyMenuState()
    {
        _titleText.Text = _navigator.Title;
        ApplySelection();
    }

    private void ApplySelection()
    {
        _refreshingSelection = true;
        foreach (var item in _navigator.Items)
        {
            item.IsSelected = false;
        }

        if (_navigator.SelectedItem is not null)
        {
            _navigator.SelectedItem.IsSelected = true;
        }

        _itemsList.ItemsSource = _navigator.Items
            .Select((item, index) => CreateRowElement(item, index == _navigator.SelectedIndex))
            .ToArray();
        _itemsList.SelectedIndex = _navigator.SelectedIndex;
        _itemsList.Focus(FocusState.Programmatic);
        _refreshingSelection = false;
    }

    private void ItemsList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshingSelection || _itemsList.SelectedIndex < 0)
        {
            return;
        }

        _navigator.Select(_itemsList.SelectedIndex);
        ApplySelection();
    }

    private void ShowChildMenu(QuickMenuItem item)
    {
        var children = item.GetChildren();
        if (children.Count == 0)
        {
            return;
        }

        _childMenuWindow?.Close();
        var position = GetChildMenuPosition();
        _childMenuWindow = new QuickMenuWindow(
            item.Title,
            children,
            _theme,
            simpleMode: true,
            parentMenu: this,
            initialPosition: position,
            onInvoked: CloseCascade);
        _childMenuWindow.Closed += (_, _) =>
        {
            _childMenuWindow = null;
        };
        _childMenuWindow.Activate();
        _childMenuWindow.FocusMenu();
    }

    private PointInt32 GetChildMenuPosition()
    {
        if (_appWindow is null)
        {
            var point = Forms.Cursor.Position;
            return new PointInt32(point.X + _windowWidth - 8, point.Y);
        }

        var yOffset = Math.Max(0, _navigator.SelectedIndex) * 32 + 4;
        return new PointInt32(_appWindow.Position.X + _windowWidth - 8, _appWindow.Position.Y + yOffset);
    }

    private void InvokeAfterClose(Action action)
    {
        if (_isClosing)
        {
            return;
        }

        _isClosing = true;
        _onInvoked?.Invoke();
        _appWindow?.Hide();
        _ = Task.Delay(90).ContinueWith(_ => DispatcherQueue.TryEnqueue(() =>
        {
            action();
            Close();
        }));
    }

    private void CloseMenu()
    {
        if (_isClosing)
        {
            return;
        }

        _isClosing = true;
        _childMenuWindow?.Close();
        Close();
    }

    private void CloseCascade()
    {
        if (_isClosing)
        {
            return;
        }

        _isClosing = true;
        _childMenuWindow?.Close();
        Close();
    }

    private void PositionNearCursor()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var id = Win32Interop.GetWindowIdFromWindow(hwnd);
        _appWindow = AppWindow.GetFromWindowId(id);
        var point = Forms.Cursor.Position;
        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }

        _appWindow.Resize(new SizeInt32(_windowWidth, _windowHeight));
        _appWindow.Move(_initialPosition ?? new PointInt32(point.X, point.Y));
    }

    private static int CalculateWindowHeight(IReadOnlyList<QuickMenuItem> items, bool simpleMode)
    {
        var rows = items.Sum(item => item.IsSeparator ? 11 : 32);
        var chrome = simpleMode ? 8 : 42;
        return Math.Clamp(rows + chrome, 44, simpleMode ? 520 : 560);
    }

    private static SolidColorBrush Brush(string color)
    {
        if (color == "#00000000")
        {
            return new SolidColorBrush(Colors.Transparent);
        }

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
