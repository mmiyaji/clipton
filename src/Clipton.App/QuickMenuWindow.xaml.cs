using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Media.Imaging;
using Brush = System.Windows.Media.Brush;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace Clipton.App;

public sealed partial class QuickMenuWindow : Window
{
    private readonly QuickMenuNavigator _navigator;
    private readonly bool _simpleMode;
    private bool _isClosing;

    public QuickMenuWindow(string title, IReadOnlyList<QuickMenuItem> items, string theme, bool simpleMode = false, string keyboardHelpText = "")
    {
        _navigator = new QuickMenuNavigator(title, items);
        _simpleMode = simpleMode;
        InitializeComponent();
        KeyboardHelpText.Text = keyboardHelpText;
        ApplyTheme(theme);
        ApplyMode();
        ApplyMenuState();
        Loaded += (_, _) =>
        {
            FocusMenu();
        };
    }

    public void FocusMenu()
    {
        Topmost = true;
        Activate();
        Focus();
        ItemsList.Focus();
        Keyboard.Focus(ItemsList);

        Dispatcher.BeginInvoke(() =>
        {
            Activate();
            ItemsList.Focus();
            Keyboard.Focus(ItemsList);
        }, DispatcherPriority.ApplicationIdle);
    }

    public bool IsSimpleMode => _simpleMode;

    private void ItemsList_OnKeyDown(object sender, KeyEventArgs e)
    {
        HandleMenuKey(e);
    }

    private void ItemsList_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var item = FindAncestor<ListBoxItem>((DependencyObject)e.OriginalSource);
        if (item is null)
        {
            return;
        }

        item.IsSelected = true;
        InvokeSelected(asPlainText: false);
    }

    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1)
        {
            DragMove();
        }
    }

    private void Window_OnDeactivated(object sender, EventArgs e)
    {
        CloseMenu();
    }

    private void Window_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!ItemsList.IsKeyboardFocusWithin)
        {
            ItemsList.Focus();
            Keyboard.Focus(ItemsList);
        }

        HandleMenuKey(e);
    }

    private void HandleMenuKey(KeyEventArgs e)
    {
        if (e.Handled)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter:
                InvokeSelected(asPlainText: false);
                e.Handled = true;
                break;
            case Key.T:
                InvokeSelected(asPlainText: true);
                e.Handled = true;
                break;
            case Key.Escape:
                if (!NavigateBack())
                {
                    CloseMenu();
                }

                e.Handled = true;
                break;
            case Key.Back:
            case Key.Left:
                NavigateBack();
                e.Handled = true;
                break;
            case Key.Right:
                OpenSelectedFolder();
                e.Handled = true;
                break;
            case Key.Up:
                MoveSelection(-1);
                e.Handled = true;
                break;
            case Key.Down:
                MoveSelection(1);
                e.Handled = true;
                break;
        }
    }

    private void InvokeSelected(bool asPlainText)
    {
        _navigator.Select(ItemsList.SelectedIndex);
        if (_navigator.SelectedItem is not QuickMenuItem item || !item.IsEnabled)
        {
            return;
        }

        var action = asPlainText ? item.PlainTextInvoke : item.Invoke;
        if (!asPlainText && _navigator.OpenSelectedFolder())
        {
            ApplyMenuState();
            return;
        }

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

    private void ApplyMenuState()
    {
        TitleText.Text = _navigator.Title;
        ItemsList.ItemsSource = _navigator.Items;
        ApplySelection();
    }

    private void OpenSelectedFolder()
    {
        _navigator.Select(ItemsList.SelectedIndex);
        if (_navigator.OpenSelectedFolder())
        {
            ApplyMenuState();
        }
    }

    private void ApplySelection()
    {
        ItemsList.SelectedIndex = _navigator.SelectedIndex;
        if (_navigator.SelectedItem is not null)
        {
            ItemsList.ScrollIntoView(_navigator.SelectedItem);
        }

        ItemsList.Focus();
        Keyboard.Focus(ItemsList);
    }

    private bool NavigateBack()
    {
        if (!_navigator.NavigateBack())
        {
            return false;
        }

        ApplyMenuState();
        return true;
    }

    private static T? FindAncestor<T>(DependencyObject? current)
        where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = current is Visual or Visual3D
                ? VisualTreeHelper.GetParent(current)
                : null;
        }

        return null;
    }

    private void CloseMenu()
    {
        if (_isClosing)
        {
            return;
        }

        _isClosing = true;
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                Close();
            }
            catch (InvalidOperationException)
            {
            }
        }, DispatcherPriority.Background);
    }

    private void InvokeAfterClose(Action action)
    {
        if (_isClosing)
        {
            return;
        }

        _isClosing = true;
        Hide();

        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(90)
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            action();
            try
            {
                Close();
            }
            catch (InvalidOperationException)
            {
            }
        };
        timer.Start();
    }

    private void ApplyTheme(string theme)
    {
        var dark = string.Equals(theme, "dark", StringComparison.OrdinalIgnoreCase);
        if (dark)
        {
            SetBrush("QuickPanelBrush", "#171D24");
            SetBrush("QuickBorderBrush", "#3A4653");
            SetBrush("QuickTextBrush", "#F1F5F9");
            SetBrush("QuickSecondaryTextBrush", "#A7B0BA");
            SetBrush("QuickSelectionBrush", "#1E3A56");
            SetBrush("QuickItemBrush", "#1E2630");
            SetBrush("QuickItemBorderBrush", "#2B3540");
            SetBrush("QuickFocusBorderBrush", "#4EA1FF");
            SetBrush("QuickPreviewBrush", "#222B34");
            SetBrush("QuickPreviewBorderBrush", "#3A4653");
            SetBrush("QuickFolderBrush", "#1B3147");
            SetBrush("QuickFolderRowBrush", "#172A3D");
            SetBrush("QuickFolderBorderBrush", "#315A82");
            SetBrush("QuickFolderTextBrush", "#9FD0FF");
            return;
        }

        SetBrush("QuickPanelBrush", "#F9FAFB");
        SetBrush("QuickBorderBrush", "#9AA5B1");
        SetBrush("QuickTextBrush", "#111827");
        SetBrush("QuickSecondaryTextBrush", "#6B7280");
        SetBrush("QuickSelectionBrush", "#DDEBFA");
        SetBrush("QuickItemBrush", "#FFFFFF");
        SetBrush("QuickItemBorderBrush", "#E3E8EF");
        SetBrush("QuickFocusBorderBrush", "#6AA7E8");
        SetBrush("QuickPreviewBrush", "#E5E7EB");
        SetBrush("QuickPreviewBorderBrush", "#CAD3DD");
        SetBrush("QuickFolderBrush", "#EEF5FF");
        SetBrush("QuickFolderRowBrush", "#F3F8FF");
        SetBrush("QuickFolderBorderBrush", "#BCD7F6");
        SetBrush("QuickFolderTextBrush", "#174A7C");
    }

    private void SetBrush(string key, string color)
    {
        Resources[key] = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
    }

    private void ApplyMode()
    {
        if (!_simpleMode)
        {
            Width = 386;
            HeaderBar.Visibility = Visibility.Visible;
            RootBorder.CornerRadius = new CornerRadius(8);
            RootBorder.Padding = new Thickness(8);
            return;
        }

        Width = 332;
        HeaderBar.Visibility = Visibility.Collapsed;
        RootBorder.CornerRadius = new CornerRadius(8);
        RootBorder.Padding = new Thickness(4);
        ItemsList.MinHeight = 28;
        ItemsList.MaxHeight = 520;
        SetBrush("QuickPanelBrush", "#2B2B2B");
        SetBrush("QuickBorderBrush", "#454545");
        SetBrush("QuickTextBrush", "#F2F2F2");
        SetBrush("QuickSecondaryTextBrush", "#C8C8C8");
        SetBrush("QuickSelectionBrush", "#3A3A3A");
        SetBrush("QuickItemBrush", "#2B2B2B");
        SetBrush("QuickItemBorderBrush", "#2B2B2B");
        SetBrush("QuickFocusBorderBrush", "#3A3A3A");
        SetBrush("QuickPreviewBrush", "#343434");
        SetBrush("QuickPreviewBorderBrush", "#474747");
        SetBrush("QuickFolderBrush", "#2B2B2B");
        SetBrush("QuickFolderRowBrush", "#2B2B2B");
        SetBrush("QuickFolderBorderBrush", "#2B2B2B");
        SetBrush("QuickFolderTextBrush", "#7DCCFF");
    }
}

public sealed record QuickMenuItem(
    string Title,
    string Subtitle,
    string KindLabel,
    string CommandHint,
    Brush AccentBrush,
    Action Invoke,
    Action? PlainTextInvoke = null,
    bool IsEnabled = true,
    ImageSource? PreviewImage = null,
    IReadOnlyList<QuickMenuItem>? Children = null,
    Func<IReadOnlyList<QuickMenuItem>>? LazyChildren = null)
{
    private IReadOnlyList<QuickMenuItem>? _resolvedChildren = Children;

    public bool IsFolder => _resolvedChildren is { Count: > 0 } || LazyChildren is not null;

    public Visibility PreviewVisibility => PreviewImage is null ? Visibility.Collapsed : Visibility.Visible;

    public Visibility IconVisibility => PreviewImage is null && !IsFolder ? Visibility.Visible : Visibility.Collapsed;

    public Visibility FolderIconVisibility => IsFolder ? Visibility.Visible : Visibility.Collapsed;

    public Visibility CommandHintVisibility => IsFolder ? Visibility.Collapsed : Visibility.Visible;

    public Visibility FolderChevronVisibility => IsFolder ? Visibility.Visible : Visibility.Collapsed;

    public Brush RowBackground => IsFolder
        ? new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#14005FB8"))
        : System.Windows.Media.Brushes.Transparent;

    public Brush RowBorderBrush => IsFolder
        ? new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#33005FB8"))
        : System.Windows.Media.Brushes.Transparent;

    public Thickness RowBorderThickness => IsFolder ? new Thickness(1) : new Thickness(0);

    public IReadOnlyList<QuickMenuItem> GetChildren()
    {
        if (_resolvedChildren is not null)
        {
            return _resolvedChildren;
        }

        _resolvedChildren = LazyChildren?.Invoke() ?? [];
        return _resolvedChildren;
    }
}
