namespace Clipton.App;

public sealed class QuickMenuNavigator
{
    private readonly Stack<(string Title, IReadOnlyList<QuickMenuItem> Items, int SelectedIndex)> _backStack = new();

    public QuickMenuNavigator(string title, IReadOnlyList<QuickMenuItem> items)
    {
        Title = title;
        Items = items;
        SelectedIndex = FirstEnabledIndex(items);
    }

    public string Title { get; private set; }

    public IReadOnlyList<QuickMenuItem> Items { get; private set; }

    public int SelectedIndex { get; private set; }

    public QuickMenuItem? SelectedItem => SelectedIndex >= 0 && SelectedIndex < Items.Count ? Items[SelectedIndex] : null;

    public void Select(int index)
    {
        if (index >= 0 && index < Items.Count && Items[index].IsEnabled)
        {
            SelectedIndex = index;
        }
    }

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
