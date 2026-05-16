using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
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

            ItemsList.Focus();
            Keyboard.Focus(ItemsList);
        };
    }

    private void ItemsList_OnKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                InvokeSelected();
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

    private void ItemsList_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        InvokeSelected();
    }

    private void Window_OnDeactivated(object sender, EventArgs e)
    {
        Close();
    }

    private void InvokeSelected()
    {
        if (ItemsList.SelectedItem is not QuickMenuItem item || !item.IsEnabled)
        {
            return;
        }

        Close();
        item.Invoke();
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
}

public sealed record QuickMenuItem(
    string Title,
    string Subtitle,
    Brush AccentBrush,
    Action Invoke,
    bool IsEnabled = true);
