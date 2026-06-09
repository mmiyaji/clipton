using Clipton.Core;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI;
using WinRT.Interop;

namespace Clipton.WinUI;

internal sealed class RichQuickMenuWindow : Window, IQuickMenuHostWindow
{
    private const int ScreenEdgePadding = 8;
    private const int MenuWidth = 392;
    private const int PreviewWidth = 320;
    private const int WindowHeight = 526;
    private const int WindowGap = 10;
    private readonly string _title;
    private readonly string _theme;
    private readonly QuickMenuShortcutSettings _shortcuts;
    private readonly Action _openHistory;
    private readonly string _showAllHistoryText;
    private readonly string _previewImageText;
    private readonly string _copyFeedbackText;
    private readonly string _cutFeedbackText;
    private readonly Grid _root = new();
    private readonly StackPanel _itemHost = new() { Spacing = 6 };
    private readonly Border _previewCard = new();
    private readonly Image _previewImage = new() { Stretch = Stretch.UniformToFill };
    private readonly TextBlock _previewMetaText = Text(12, 0.76);
    private readonly Border _feedbackPill = new();
    private readonly TextBlock _feedbackText = Text(12, 0.94);
    private readonly DispatcherTimer _feedbackTimer = new() { Interval = TimeSpan.FromMilliseconds(900) };
    private IReadOnlyList<QuickMenuItem> _items = [];
    private readonly List<QuickMenuItem> _visibleItems = [];
    private readonly List<Border> _itemCards = [];
    private int _selectedIndex;
    private AppWindow? _appWindow;
    private IntPtr _hwnd;
    private bool _dismissed;
    private long _previewRequestId;

    public RichQuickMenuWindow(
        string title,
        IReadOnlyList<QuickMenuItem> items,
        string theme,
        QuickMenuShortcutSettings shortcuts,
        Action openHistory,
        string showAllHistoryText,
        string previewImageText,
        string copyFeedbackText,
        string cutFeedbackText)
    {
        _title = title;
        _items = items;
        _theme = theme;
        _shortcuts = shortcuts;
        _openHistory = openHistory;
        _showAllHistoryText = showAllHistoryText;
        _previewImageText = previewImageText;
        _copyFeedbackText = copyFeedbackText;
        _cutFeedbackText = cutFeedbackText;

        InitializeWindow();
        BuildContent();
        RebuildItems();
        _feedbackTimer.Tick += (_, _) =>
        {
            _feedbackTimer.Stop();
            _feedbackPill.Visibility = Visibility.Collapsed;
        };
    }

    public event EventHandler? Dismissed;

    public string DisplayMode => "rich";

    public void FocusMenu()
    {
        Activate();
        PositionNearCursor();
        _root.Focus(FocusState.Programmatic);
    }

    public void Reopen(IReadOnlyList<QuickMenuItem> items)
    {
        _items = items;
        _selectedIndex = 0;
        RebuildItems();
        PositionNearCursor();
        FocusMenu();
    }

    public void Dismiss()
    {
        if (_dismissed)
        {
            return;
        }

        _dismissed = true;
        _feedbackTimer.Stop();
        DispatcherQueue.TryEnqueue(() =>
        {
            _appWindow?.Hide();
            Dismissed?.Invoke(this, EventArgs.Empty);
        });
    }

    private void InitializeWindow()
    {
        ExtendsContentIntoTitleBar = true;
        Content = _root;
        _root.KeyDown += OnKeyDown;
        _root.Background = Brush(14, 14, 14);
        _hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.SetBorderAndTitleBar(false, false);
        }

        _appWindow.Resize(new SizeInt32(MenuWidth, WindowHeight));
    }

    private void BuildContent()
    {
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(MenuWidth) });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(WindowGap) });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(PreviewWidth) });
        _root.Padding = new Thickness(0);

        var menuCard = new Border
        {
            Width = MenuWidth,
            Height = WindowHeight,
            CornerRadius = new CornerRadius(9),
            BorderThickness = new Thickness(1),
            BorderBrush = Brush(74, 74, 74),
            Background = Brush(37, 37, 37),
            Padding = new Thickness(10),
            Child = BuildMenuPanel()
        };
        _root.Children.Add(menuCard);

        _previewCard.Width = PreviewWidth;
        _previewCard.Height = 338;
        _previewCard.CornerRadius = new CornerRadius(9);
        _previewCard.BorderThickness = new Thickness(1);
        _previewCard.BorderBrush = Brush(68, 68, 68);
        _previewCard.Background = Brush(44, 44, 44);
        _previewCard.Padding = new Thickness(10);
        _previewCard.Visibility = Visibility.Collapsed;
        _previewCard.VerticalAlignment = VerticalAlignment.Center;
        _previewCard.Child = BuildPreviewPanel();
        Grid.SetColumn(_previewCard, 2);
        _root.Children.Add(_previewCard);
    }

    private UIElement BuildMenuPanel()
    {
        var panel = new Grid();
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var toolbar = new Grid { Height = 42, Margin = new Thickness(0, 0, 0, 8) };
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        AddToolbarButton(toolbar, 0, "\uE8C8", _title, selected: true, null);
        AddToolbarButton(toolbar, 1, "\uE718", "Pinned", selected: false, null);
        AddToolbarButton(toolbar, 2, "\uE8D2", "Text", selected: false, null);
        AddToolbarButton(toolbar, 3, "\uEB9F", _previewImageText, selected: false, null);
        AddToolbarButton(toolbar, 5, "\uE711", "Close", selected: false, Dismiss);
        panel.Children.Add(toolbar);

        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _itemHost
        };
        Grid.SetRow(scrollViewer, 1);
        panel.Children.Add(scrollViewer);

        var allHistoryButton = new Button
        {
            Content = _showAllHistoryText,
            Height = 52,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 8, 0, 0),
            Background = Brush(43, 43, 43),
            BorderBrush = Brush(65, 65, 65),
            Foreground = Brush(246, 246, 246)
        };
        allHistoryButton.Click += (_, _) =>
        {
            Dismiss();
            _openHistory();
        };
        Grid.SetRow(allHistoryButton, 2);
        panel.Children.Add(allHistoryButton);

        return panel;
    }

    private UIElement BuildPreviewPanel()
    {
        var panel = new Grid();
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(226) });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var imageFrame = new Border
        {
            CornerRadius = new CornerRadius(5),
            Background = Brush(30, 30, 30),
            Clip = new RectangleGeometry { Rect = new Rect(0, 0, 298, 226) },
            Child = _previewImage
        };
        panel.Children.Add(imageFrame);

        _previewMetaText.Margin = new Thickness(0, 10, 0, 12);
        Grid.SetRow(_previewMetaText, 1);
        panel.Children.Add(_previewMetaText);

        var actions = new Grid { ColumnSpacing = 8 };
        for (var i = 0; i < 4; i++)
        {
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        AddPreviewAction(actions, 0, "\uE8A7", "Paste", InvokeSelected);
        AddPreviewAction(actions, 1, "\uE8C8", _copyFeedbackText, CopySelectedImage);
        AddPreviewAction(actions, 2, "\uE8E5", "Plain text", InvokeSelectedPlainText);
        AddPreviewAction(actions, 3, "\uE74D", _cutFeedbackText, CutSelectedImage);
        Grid.SetRow(actions, 2);
        panel.Children.Add(actions);

        _feedbackPill.Background = Brush(20, 20, 20);
        _feedbackPill.BorderBrush = Brush(74, 74, 74);
        _feedbackPill.BorderThickness = new Thickness(1);
        _feedbackPill.CornerRadius = new CornerRadius(14);
        _feedbackPill.Padding = new Thickness(10, 5, 10, 5);
        _feedbackPill.HorizontalAlignment = HorizontalAlignment.Right;
        _feedbackPill.VerticalAlignment = VerticalAlignment.Bottom;
        _feedbackPill.Margin = new Thickness(0, 0, 8, 48);
        _feedbackPill.Visibility = Visibility.Collapsed;
        _feedbackPill.Child = _feedbackText;
        Grid.SetRowSpan(_feedbackPill, 3);
        panel.Children.Add(_feedbackPill);

        return panel;
    }

    private void RebuildItems()
    {
        _itemHost.Children.Clear();
        _visibleItems.Clear();
        _itemCards.Clear();

        foreach (var item in _items.Where(item => !item.IsSeparator).Take(12))
        {
            _visibleItems.Add(item);
            var card = BuildItemCard(item, _visibleItems.Count - 1);
            _itemCards.Add(card);
            _itemHost.Children.Add(card);
        }

        if (_visibleItems.Count == 0)
        {
            return;
        }

        _selectedIndex = Math.Clamp(_selectedIndex, 0, _visibleItems.Count - 1);
        UpdateSelection();
    }

    private Border BuildItemCard(QuickMenuItem item, int index)
    {
        var card = new Border
        {
            Height = 76,
            CornerRadius = new CornerRadius(7),
            BorderThickness = new Thickness(1),
            BorderBrush = Brush(56, 56, 56),
            Background = Brush(40, 40, 40),
            Padding = new Thickness(8),
            Child = BuildItemContent(item)
        };
        card.PointerEntered += (_, _) => Select(index);
        card.Tapped += (_, _) => InvokeItem(item);
        return card;
    }

    private UIElement BuildItemContent(QuickMenuItem item)
    {
        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var iconFrame = new Border
        {
            Width = 84,
            Height = 60,
            CornerRadius = new CornerRadius(5),
            Background = Brush(34, 34, 34),
            Child = CreateItemIcon(item)
        };
        grid.Children.Add(iconFrame);

        var textPanel = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 4
        };
        textPanel.Children.Add(Text(TrimText(item.Title, 28), 15, 0.96, Microsoft.UI.Text.FontWeights.SemiBold));
        textPanel.Children.Add(Text(CreateItemSubtitle(item), 12, 0.66));
        Grid.SetColumn(textPanel, 1);
        grid.Children.Add(textPanel);

        var trailingGlyph = item.IsPinned ? "\uE734" : item.PasteOptions is { Count: > 0 } ? "\uE712" : "\uE734";
        var trailing = Text(trailingGlyph, 19, item.IsPinned || item.PasteOptions is { Count: > 0 } ? 0.86 : 0.48);
        trailing.FontFamily = new FontFamily("Segoe Fluent Icons");
        trailing.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(trailing, 2);
        grid.Children.Add(trailing);

        return grid;
    }

    private UIElement CreateItemIcon(QuickMenuItem item)
    {
        if (item.IconImageBytes is { Length: > 0 } bytes)
        {
            var image = new Image { Stretch = Stretch.UniformToFill };
            _ = SetImageSourceAsync(image, bytes);
            return image;
        }

        var glyph = item.IconGlyph ?? GetFallbackGlyph(item);
        return new FontIcon
        {
            Glyph = glyph,
            FontFamily = new FontFamily(item.IconFontFamily ?? "Segoe Fluent Icons"),
            FontSize = 27,
            Foreground = Brush(232, 232, 232),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private void Select(int index)
    {
        if (index < 0 || index >= _visibleItems.Count || index == _selectedIndex)
        {
            return;
        }

        _selectedIndex = index;
        UpdateSelection();
    }

    private void UpdateSelection()
    {
        for (var i = 0; i < _itemCards.Count; i++)
        {
            var selected = i == _selectedIndex;
            _itemCards[i].BorderBrush = selected ? Brush(28, 160, 230) : Brush(56, 56, 56);
            _itemCards[i].BorderThickness = selected ? new Thickness(2) : new Thickness(1);
            _itemCards[i].Background = selected ? Brush(48, 48, 48) : Brush(40, 40, 40);
        }

        _ = UpdatePreviewAsync(GetSelectedItem());
    }

    private async Task UpdatePreviewAsync(QuickMenuItem? item)
    {
        var requestId = Interlocked.Increment(ref _previewRequestId);
        var bytes = item?.PreviewImageBytesProvider?.Invoke() ?? item?.IconImageBytes;
        if (item is null || bytes is not { Length: > 0 })
        {
            _previewCard.Visibility = Visibility.Collapsed;
            ResizeForPreview(showPreview: false);
            return;
        }

        var bitmap = await CreateBitmapImageAsync(bytes, decodePixelWidth: 600);
        if (requestId != _previewRequestId)
        {
            return;
        }

        _previewImage.Source = bitmap;
        _previewMetaText.Text = CreatePreviewMeta(item, bytes.Length);
        _previewCard.Visibility = Visibility.Visible;
        ResizeForPreview(showPreview: true);
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs args)
    {
        var key = args.Key;
        var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        switch (key)
        {
            case VirtualKey.Escape:
                args.Handled = true;
                Dismiss();
                break;
            case VirtualKey.Down:
                args.Handled = true;
                Select(Math.Min(_selectedIndex + 1, _visibleItems.Count - 1));
                break;
            case VirtualKey.Up:
                args.Handled = true;
                Select(Math.Max(_selectedIndex - 1, 0));
                break;
            case VirtualKey.Enter:
                args.Handled = true;
                InvokeSelected();
                break;
            case VirtualKey.C when ctrl:
                args.Handled = true;
                CopySelectedImage();
                break;
            case VirtualKey.X when ctrl:
                args.Handled = true;
                CutSelectedImage();
                break;
            default:
                if (MatchesShortcut(_shortcuts.PastePlainText, key, ctrl))
                {
                    args.Handled = true;
                    InvokeSelectedPlainText();
                }

                break;
        }
    }

    private void InvokeSelected() => InvokeItem(GetSelectedItem());

    private void InvokeSelectedPlainText()
    {
        var item = GetSelectedItem();
        if (item?.PlainTextInvoke is null)
        {
            InvokeItem(item);
            return;
        }

        Dismiss();
        item.PlainTextInvoke();
    }

    private void InvokeItem(QuickMenuItem? item)
    {
        if (item is null || !item.IsEnabled)
        {
            return;
        }

        if (item.IsFolder)
        {
            Reopen(item.GetChildren());
            return;
        }

        Dismiss();
        item.Invoke();
    }

    private void CopySelectedImage()
    {
        var item = GetSelectedItem();
        if (item?.CopyInvoke is null)
        {
            return;
        }

        item.CopyInvoke();
        ShowFeedback(_copyFeedbackText);
    }

    private void CutSelectedImage()
    {
        var item = GetSelectedItem();
        if (item?.CutInvoke is null)
        {
            return;
        }

        item.CutInvoke();
        ShowFeedback(_cutFeedbackText);
        Dismiss();
    }

    private void ShowFeedback(string text)
    {
        _feedbackText.Text = text;
        _feedbackPill.Visibility = Visibility.Visible;
        _feedbackTimer.Stop();
        _feedbackTimer.Start();
    }

    private QuickMenuItem? GetSelectedItem()
    {
        return _selectedIndex >= 0 && _selectedIndex < _visibleItems.Count ? _visibleItems[_selectedIndex] : null;
    }

    private void ResizeForPreview(bool showPreview)
    {
        var width = showPreview ? MenuWidth + WindowGap + PreviewWidth : MenuWidth;
        _root.ColumnDefinitions[1].Width = showPreview ? new GridLength(WindowGap) : new GridLength(0);
        _root.ColumnDefinitions[2].Width = showPreview ? new GridLength(PreviewWidth) : new GridLength(0);
        _appWindow?.Resize(new SizeInt32(width, WindowHeight));
        PositionNearCursor();
    }

    private void PositionNearCursor()
    {
        if (_appWindow is null)
        {
            return;
        }

        var width = _previewCard.Visibility == Visibility.Visible ? MenuWidth + WindowGap + PreviewWidth : MenuWidth;
        var cursor = NativeMethods.GetCursorPos(out var point) ? point : new NativeMethods.Point { X = 200, Y = 200 };
        var workArea = GetWorkArea(cursor);
        var x = Math.Clamp(cursor.X - 18, workArea.Left + ScreenEdgePadding, workArea.Right - width - ScreenEdgePadding);
        var y = Math.Clamp(cursor.Y - 18, workArea.Top + ScreenEdgePadding, workArea.Bottom - WindowHeight - ScreenEdgePadding);
        _appWindow.Move(new PointInt32(x, y));
    }

    private static NativeMethods.Rect GetWorkArea(NativeMethods.Point point)
    {
        var monitor = NativeMethods.MonitorFromPoint(point, NativeMethods.MonitorDefaultToNearest);
        var info = new NativeMethods.MonitorInfo
        {
            Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MonitorInfo>()
        };

        return monitor != IntPtr.Zero && NativeMethods.GetMonitorInfo(monitor, ref info)
            ? info.Work
            : new NativeMethods.Rect { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };
    }

    private void AddToolbarButton(Grid toolbar, int column, string glyph, string tooltip, bool selected, Action? action)
    {
        var button = IconButton(glyph, tooltip);
        button.Margin = new Thickness(2, 0, 8, 0);
        button.Background = selected ? Brush(36, 79, 102) : Brush(37, 37, 37);
        button.BorderBrush = selected ? Brush(23, 150, 214) : Brush(37, 37, 37);
        if (action is not null)
        {
            button.Click += (_, _) => action();
        }

        Grid.SetColumn(button, column);
        toolbar.Children.Add(button);
    }

    private void AddPreviewAction(Grid actions, int column, string glyph, string tooltip, Action action)
    {
        var button = IconButton(glyph, tooltip);
        button.Height = 42;
        button.Background = Brush(58, 58, 58);
        button.BorderBrush = Brush(68, 68, 68);
        button.Click += (_, _) => action();
        Grid.SetColumn(button, column);
        actions.Children.Add(button);
    }

    private static Button IconButton(string glyph, string tooltip)
    {
        var button = new Button
        {
            Width = 42,
            Height = 38,
            Padding = new Thickness(0),
            Foreground = Brush(236, 236, 236),
            Content = new FontIcon
            {
                Glyph = glyph,
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                FontSize = 18
            }
        };
        ToolTipService.SetToolTip(button, tooltip);
        AutomationProperties.SetName(button, tooltip);
        return button;
    }

    private static TextBlock Text(string text, double size, double opacity, Windows.UI.Text.FontWeight? weight = null)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = size,
            FontWeight = weight ?? Microsoft.UI.Text.FontWeights.Normal,
            Foreground = Brush(245, 245, 245),
            Opacity = opacity,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1
        };
    }

    private static TextBlock Text(double size, double opacity) => Text(string.Empty, size, opacity);

    private static SolidColorBrush Brush(byte r, byte g, byte b) => new(Color.FromArgb(255, r, g, b));

    private static string TrimText(string text, int max)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length <= max)
        {
            return text;
        }

        return $"{text[..Math.Max(0, max - 1)]}...";
    }

    private static string CreateItemSubtitle(QuickMenuItem item)
    {
        if (item.CapturedAt is { } capturedAt)
        {
            return CreateRelativeTime(capturedAt);
        }

        return item.Subtitle;
    }

    private static string CreateRelativeTime(DateTimeOffset capturedAt)
    {
        var elapsed = DateTimeOffset.UtcNow - capturedAt.ToUniversalTime();
        if (elapsed.TotalSeconds < 5)
        {
            return "Just now";
        }

        if (elapsed.TotalSeconds < 60)
        {
            return $"{Math.Max(1, (int)elapsed.TotalSeconds)}s ago";
        }

        if (elapsed.TotalMinutes < 60)
        {
            return $"{Math.Max(1, (int)elapsed.TotalMinutes)}m ago";
        }

        return elapsed.TotalHours < 24
            ? $"{Math.Max(1, (int)elapsed.TotalHours)}h ago"
            : capturedAt.LocalDateTime.ToString("yyyy/MM/dd HH:mm");
    }

    private static string CreatePreviewMeta(QuickMenuItem item, int byteLength)
    {
        var size = byteLength >= 1024 * 1024
            ? $"{byteLength / 1024d / 1024d:0.0} MB"
            : $"{Math.Max(1, byteLength / 1024)} KB";
        return $"{TrimText(item.Title, 18)}    {size}";
    }

    private static string GetFallbackGlyph(QuickMenuItem item)
    {
        return item.KindLabel.Equals("Image", StringComparison.OrdinalIgnoreCase)
            ? "\uEB9F"
            : item.KindLabel.Equals("Link", StringComparison.OrdinalIgnoreCase)
                ? "\uE71B"
                : item.KindLabel.Equals("Code", StringComparison.OrdinalIgnoreCase)
                    ? "\uE943"
                    : "\uE8A5";
    }

    private static bool MatchesShortcut(string shortcut, VirtualKey key, bool ctrl)
    {
        if (string.IsNullOrWhiteSpace(shortcut))
        {
            return false;
        }

        var normalized = shortcut.Trim();
        var requiresCtrl = normalized.StartsWith("Ctrl+", StringComparison.OrdinalIgnoreCase);
        var keyText = requiresCtrl ? normalized[5..] : normalized;
        return ctrl == requiresCtrl
            && Enum.TryParse<VirtualKey>(keyText, ignoreCase: true, out var parsedKey)
            && parsedKey == key;
    }

    private static async Task SetImageSourceAsync(Image image, byte[] bytes)
    {
        image.Source = await CreateBitmapImageAsync(bytes, decodePixelWidth: 180);
    }

    private static async Task<BitmapImage> CreateBitmapImageAsync(byte[] bytes, int decodePixelWidth = 0)
    {
        var bitmap = new BitmapImage();
        if (decodePixelWidth > 0)
        {
            bitmap.DecodePixelWidth = decodePixelWidth;
        }

        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(bytes);
            await writer.StoreAsync();
            await writer.FlushAsync();
        }

        stream.Seek(0);
        await bitmap.SetSourceAsync(stream);
        return bitmap;
    }
}
