using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Brush = System.Windows.Media.Brush;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace Clipton.App;

public sealed partial class QuickMenuWindow : Window
{
    private readonly IReadOnlyList<QuickMenuItem> _items;

    public QuickMenuWindow(string title, IReadOnlyList<QuickMenuItem> items)
    {
        _items = items;
        InitializeComponent();
        TitleText.Text = title;
        ItemsList.ItemsSource = _items;
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
                Close();
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

    private void Window_OnDeactivated(object sender, EventArgs e)
    {
        Close();
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
        if (action is null)
        {
            return;
        }

        Close();
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
}

public sealed record QuickMenuItem(
    string Title,
    string Subtitle,
    string KindLabel,
    string CommandHint,
    Brush AccentBrush,
    Action Invoke,
    Action? PlainTextInvoke = null,
    bool IsEnabled = true);
