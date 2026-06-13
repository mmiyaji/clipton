namespace Clipton.WinUI;

internal interface IQuickMenuHostWindow
{
    event EventHandler? Dismissed;

    string DisplayMode { get; }

    bool IsDismissed { get; }

    void FocusMenu();

    void Reopen(IReadOnlyList<QuickMenuItem> items);

    void Dismiss();
}
