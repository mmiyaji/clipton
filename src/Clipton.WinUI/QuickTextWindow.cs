using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.System;
using Windows.UI;
using WinRT.Interop;

namespace Clipton.WinUI;

internal sealed class QuickTextWindow : Window
{
    private const int WindowWidth = 560;
    private const int WindowHeight = 420;
    private const int ScreenEdgePadding = 16;
    private readonly TaskCompletionSource<string?> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TextBox _textBox = new();
    private readonly bool _editable;
    private bool _completed;

    private QuickTextWindow(
        string title,
        string text,
        string theme,
        bool editable,
        string submitText,
        string cancelText)
    {
        _editable = editable;
        Title = title;
        ExtendsContentIntoTitleBar = true;
        Content = BuildContent(title, text, theme, submitText, cancelText);
        Closed += (_, _) =>
        {
            if (!_completed)
            {
                _completion.TrySetResult(null);
            }
        };
    }

    public static Task<string?> ShowAsync(
        string title,
        string text,
        string theme,
        bool editable,
        string submitText,
        string cancelText)
    {
        var window = new QuickTextWindow(title, text, theme, editable, submitText, cancelText);
        window.PositionNearCursor();
        window.Activate();
        window._textBox.Focus(FocusState.Programmatic);
        if (editable)
        {
            window._textBox.SelectionStart = window._textBox.Text.Length;
        }

        return window._completion.Task;
    }

    private UIElement BuildContent(string title, string text, string theme, string submitText, string cancelText)
    {
        var dark = string.Equals(theme, "dark", StringComparison.OrdinalIgnoreCase);
        var background = dark ? Color.FromArgb(255, 32, 32, 32) : Colors.White;
        var foreground = dark ? Colors.White : Color.FromArgb(255, 26, 26, 26);
        var border = dark ? Color.FromArgb(255, 72, 72, 72) : Color.FromArgb(255, 210, 216, 224);

        var root = new Grid
        {
            Background = new SolidColorBrush(background),
            Padding = new Thickness(18),
            RowSpacing = 12
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.KeyDown += (_, args) => HandleKeyDown(args);

        root.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 17,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(foreground),
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        _textBox.Text = text;
        _textBox.AcceptsReturn = true;
        _textBox.TextWrapping = TextWrapping.Wrap;
        _textBox.IsReadOnly = !_editable;
        _textBox.BorderBrush = new SolidColorBrush(border);
        _textBox.Background = new SolidColorBrush(dark ? Color.FromArgb(255, 24, 24, 24) : Color.FromArgb(255, 250, 251, 252));
        _textBox.Foreground = new SolidColorBrush(foreground);
        _textBox.FontFamily = new FontFamily("Cascadia Mono, Consolas, Meiryo UI");
        _textBox.FontSize = 13;
        _textBox.Padding = new Thickness(10);
        _textBox.KeyDown += (_, args) => HandleKeyDown(args);
        Grid.SetRow(_textBox, 1);
        root.Children.Add(_textBox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };
        var cancelButton = new Button
        {
            Content = cancelText,
            MinWidth = 88
        };
        cancelButton.Click += (_, _) => Complete(null);
        buttons.Children.Add(cancelButton);

        if (_editable)
        {
            var submitButton = new Button
            {
                Content = submitText,
                MinWidth = 112
            };
            submitButton.Click += (_, _) => Complete(_textBox.Text);
            buttons.Children.Add(submitButton);
        }

        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);
        return root;
    }

    private void HandleKeyDown(KeyRoutedEventArgs args)
    {
        var ctrl = Microsoft.UI.Input.InputKeyboardSource
            .GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

        if (args.Key == VirtualKey.Escape)
        {
            args.Handled = true;
            Complete(null);
            return;
        }

        if (_editable && args.Key == VirtualKey.Enter && ctrl)
        {
            args.Handled = true;
            Complete(_textBox.Text);
        }
    }

    private void Complete(string? result)
    {
        if (_completed)
        {
            return;
        }

        _completed = true;
        _completion.TrySetResult(result);
        Close();
    }

    private void PositionNearCursor()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.Resize(new SizeInt32(WindowWidth, WindowHeight));
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = true;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }

        var point = QuickMenuWindow.GetCursorPoint();
        var workingArea = QuickMenuWindow.GetWorkingArea(point);
        var x = Math.Clamp(point.X - WindowWidth / 2, workingArea.Left + ScreenEdgePadding, workingArea.Right - WindowWidth - ScreenEdgePadding);
        var y = Math.Clamp(point.Y - 80, workingArea.Top + ScreenEdgePadding, workingArea.Bottom - WindowHeight - ScreenEdgePadding);
        appWindow.Move(new PointInt32(x, y));
    }
}
