namespace Clipton.WinUI;

/// <summary>
/// Pure navigation state for a hierarchical quick menu.
/// </summary>
/// <remarks>
/// This class contains no UI framework types so keyboard navigation can be tested without
/// opening WinUI menus. Disabled items are never selected by movement or direct selection.
/// </remarks>
public sealed class QuickMenuNavigator
{
    private readonly Stack<(string Title, IReadOnlyList<QuickMenuItem> Items, int SelectedIndex)> _backStack = new();

    public QuickMenuNavigator(string title, IReadOnlyList<QuickMenuItem> items)
    {
        Title = title;
        Items = items;
        SelectedIndex = FirstEnabledIndex(items);
    }

    /// <summary>Current folder title.</summary>
    public string Title { get; private set; }

    /// <summary>Items in the current folder.</summary>
    public IReadOnlyList<QuickMenuItem> Items { get; private set; }

    /// <summary>Selected item index, or -1 when no enabled item exists.</summary>
    public int SelectedIndex { get; private set; }

    /// <summary>Selected item, or <see langword="null"/> when selection is invalid.</summary>
    public QuickMenuItem? SelectedItem => SelectedIndex >= 0 && SelectedIndex < Items.Count ? Items[SelectedIndex] : null;

    /// <summary>Selects an enabled item by index.</summary>
    public void Select(int index)
    {
        if (index >= 0 && index < Items.Count && Items[index].IsEnabled)
        {
            SelectedIndex = index;
        }
    }

    /// <summary>
    /// Moves selection by a signed delta, wrapping and skipping disabled items.
    /// </summary>
    public bool MoveSelection(int delta)
    {
        if (Items.Count == 0)
        {
            return false;
        }

        var index = SelectedIndex < 0 ? 0 : SelectedIndex;
        for (var i = 0; i < Items.Count; i++)
        {
            index = (index + delta + Items.Count) % Items.Count;
            if (Items[index].IsEnabled)
            {
                SelectedIndex = index;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Opens the selected folder and stores current state for back navigation.
    /// </summary>
    public bool OpenSelectedFolder()
    {
        var selected = SelectedItem;
        var children = selected?.GetChildren() ?? [];
        if (children.Count == 0)
        {
            return false;
        }

        _backStack.Push((Title, Items, SelectedIndex));
        Title = selected!.Title;
        Items = children;
        SelectedIndex = FirstEnabledIndex(children);
        return true;
    }

    /// <summary>Returns to the previous folder, restoring its previous selection.</summary>
    public bool NavigateBack()
    {
        if (_backStack.Count == 0)
        {
            return false;
        }

        var previous = _backStack.Pop();
        Title = previous.Title;
        Items = previous.Items;
        SelectedIndex = previous.SelectedIndex;
        return true;
    }

    private static int FirstEnabledIndex(IReadOnlyList<QuickMenuItem> items)
    {
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].IsEnabled)
            {
                return i;
            }
        }

        return -1;
    }
}
