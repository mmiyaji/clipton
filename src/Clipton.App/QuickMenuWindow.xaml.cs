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
    private readonly string _rootTitle;
    private IReadOnlyList<QuickMenuItem> _items;
    private readonly Stack<(string Title, IReadOnlyList<QuickMenuItem> Items)> _navigationStack = new();
    private bool _isClosing;

    public QuickMenuWindow(string title, IReadOnlyList<QuickMenuItem> items, string theme)
    {
        _rootTitle = title;
        _items = items;
        InitializeComponent();
        ApplyTheme(theme);
        SetItems(title, _items);
        Loaded += (_, _) =>
        {
            if (_items.Count > 0)
            {
                ItemsList.SelectedIndex = 0;
            }

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

    private void ItemsList_OnKeyDown(object sender, KeyEventArgs e)
    {
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
                NavigateBack();
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
    }

    private void InvokeSelected(bool asPlainText)
    {
        if (ItemsList.SelectedItem is not QuickMenuItem item || !item.IsEnabled)
        {
            return;
        }

        var action = asPlainText ? item.PlainTextInvoke : item.Invoke;
        if (!asPlainText && item.Children is { Count: > 0 } children)
        {
            _navigationStack.Push((TitleText.Text, _items));
            SetItems(item.Title, children);
            return;
        }

        if (action is null)
        {
            return;
        }

        CloseMenu();
        action();
    }

    private void MoveSelection(int delta)
    {
        if (_items.Count == 0)
        {
            return;
        }

        var index = ItemsList.SelectedIndex < 0 ? 0 : ItemsList.SelectedIndex;
        for (var i = 0; i < _items.Count; i++)
        {
            index = (index + delta + _items.Count) % _items.Count;
            if (_items[index].IsEnabled)
            {
                ItemsList.SelectedIndex = index;
                ItemsList.ScrollIntoView(_items[index]);
                break;
            }
        }
    }

    private void SetItems(string title, IReadOnlyList<QuickMenuItem> items)
    {
        TitleText.Text = title;
        _items = items;
        ItemsList.ItemsSource = _items;
        ItemsList.SelectedIndex = _items.Count > 0 ? 0 : -1;
        if (_items.Count > 0)
        {
            ItemsList.ScrollIntoView(_items[0]);
        }
    }

    private bool NavigateBack()
    {
        if (_navigationStack.Count == 0)
        {
            return false;
        }

        var previous = _navigationStack.Pop();
        SetItems(previous.Title, previous.Items);
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
            SetBrush("QuickPreviewBrush", "#222B34");
            SetBrush("QuickPreviewBorderBrush", "#3A4653");
            return;
        }

        SetBrush("QuickPanelBrush", "#F9FAFB");
        SetBrush("QuickBorderBrush", "#9AA5B1");
        SetBrush("QuickTextBrush", "#111827");
        SetBrush("QuickSecondaryTextBrush", "#6B7280");
        SetBrush("QuickSelectionBrush", "#DDEBFA");
        SetBrush("QuickPreviewBrush", "#E5E7EB");
        SetBrush("QuickPreviewBorderBrush", "#CAD3DD");
    }

    private void SetBrush(string key, string color)
    {
        Resources[key] = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
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
    IReadOnlyList<QuickMenuItem>? Children = null)
{
    public Visibility PreviewVisibility => PreviewImage is null ? Visibility.Collapsed : Visibility.Visible;

    public Visibility IconVisibility => PreviewImage is null ? Visibility.Visible : Visibility.Collapsed;
}
