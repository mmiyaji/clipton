using Clipton.Core;
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

// Launcher-style incremental search over the quick menu items. The search
// source is flattened once by the owning QuickMenuWindow; every keystroke only
// filters the in-memory snapshot, so no clipboard or store access happens here.
internal sealed class QuickMenuSearchWindow : Window
{
    private const int WindowWidth = 600;
    private const int WindowHeight = 480;
    private const int MaxResults = 30;
    private const int DebounceMilliseconds = 150;

    private enum CloseAction
    {
        DismissAll,
        ReturnToMenu,
        None
    }

    private readonly IReadOnlyList<QuickMenuItem> _searchSource;
    private readonly string _noSearchResultsText;
    private readonly Action<QuickMenuItem, bool> _invokeItem;
    private readonly Action _openDetailedSearch;
    private readonly Action _returnToMenu;
    private readonly Action _dismissAll;
    private readonly TextBox _searchBox = new();
    private readonly ListView _resultsList = new();
    private readonly TextBlock _emptyText = new();
    private readonly Dictionary<QuickMenuItem, ListViewItem> _rowCache = new();
    private readonly bool _dark;
    private IntPtr _hwnd;
    private long _queryVersion;
    private bool _closing;
    private CloseAction _closeAction = CloseAction.DismissAll;

    public QuickMenuSearchWindow(
        IReadOnlyList<QuickMenuItem> searchSource,
        string theme,
        string searchPrompt,
        string searchHintsText,
        string advancedSearchButtonText,
        string noSearchResultsText,
        Action<QuickMenuItem, bool> invokeItem,
        Action openDetailedSearch,
        Action returnToMenu,
        Action dismissAll)
    {
        _searchSource = searchSource;
        _noSearchResultsText = noSearchResultsText;
        _invokeItem = invokeItem;
        _openDetailedSearch = openDetailedSearch;
        _returnToMenu = returnToMenu;
        _dismissAll = dismissAll;
        _dark = string.Equals(theme, "dark", StringComparison.OrdinalIgnoreCase);
        Title = "Clipton";
        Content = BuildContent(searchPrompt, searchHintsText, advancedSearchButtonText);
        Closed += (_, _) =>
        {
            _closing = true;
            _queryVersion++;
            switch (_closeAction)
            {
                case CloseAction.ReturnToMenu:
                    _returnToMenu();
                    break;
                case CloseAction.DismissAll:
                    _dismissAll();
                    break;
            }
        };
        Activated += (_, args) =>
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated && !_closing)
            {
                CloseWith(CloseAction.DismissAll);
            }
        };
    }

    public void Show()
    {
        // Size, position and chrome must be final before Activate; otherwise a
        // default-sized titled window flashes briefly on screen.
        _hwnd = WindowNative.GetWindowHandle(this);
        PositionNearCursor();
        QuickMenuWindow.ConfigureBorderlessToolWindow(_hwnd);
        Activate();
        NativeMethods.SetForegroundWindow(_hwnd);
        FocusSearchBox();
        _ = Task.Delay(120).ContinueWith(_ => DispatcherQueue.TryEnqueue(FocusSearchBox));
        RefreshResults();
    }

    public void CloseQuietly()
    {
        CloseWith(CloseAction.None);
    }

    private void CloseWith(CloseAction action)
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        _closeAction = action;
        Close();
    }

    private void FocusSearchBox()
    {
        _searchBox.Focus(FocusState.Programmatic);
        _searchBox.Select(_searchBox.Text.Length, 0);
    }

    private Grid BuildContent(string searchPrompt, string searchHintsText, string advancedSearchButtonText)
    {
        var root = new Grid
        {
            RequestedTheme = _dark ? ElementTheme.Dark : ElementTheme.Light,
            Background = new SolidColorBrush(_dark ? Color.FromArgb(255, 31, 31, 31) : Color.FromArgb(255, 243, 243, 243)),
            Padding = new Thickness(14)
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _searchBox.PlaceholderText = searchPrompt;
        _searchBox.MinHeight = 36;
        _searchBox.VerticalContentAlignment = VerticalAlignment.Center;
        _searchBox.Margin = new Thickness(0, 0, 0, 10);
        _searchBox.TextChanged += (_, _) => OnQueryChanged();
        _searchBox.KeyDown += OnSearchBoxKeyDown;
        root.Children.Add(_searchBox);

        _resultsList.SelectionMode = ListViewSelectionMode.Single;
        _resultsList.IsItemClickEnabled = true;
        _resultsList.ItemClick += (_, args) =>
        {
            if (args.ClickedItem is ListViewItem { Tag: QuickMenuItem item })
            {
                InvokeItem(item, asPlainText: false);
            }
        };
        Grid.SetRow(_resultsList, 1);
        root.Children.Add(_resultsList);

        _emptyText.Text = _noSearchResultsText;
        _emptyText.Visibility = Visibility.Collapsed;
        _emptyText.Opacity = 0.6;
        _emptyText.HorizontalAlignment = HorizontalAlignment.Center;
        _emptyText.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetRow(_emptyText, 1);
        root.Children.Add(_emptyText);

        var footer = new Grid { Margin = new Thickness(2, 10, 2, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var hints = new TextBlock
        {
            Text = searchHintsText,
            FontSize = 12,
            Opacity = 0.6,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        footer.Children.Add(hints);
        var advancedButton = new HyperlinkButton
        {
            Content = advancedSearchButtonText,
            FontSize = 12,
            Padding = new Thickness(6, 2, 6, 2)
        };
        advancedButton.Click += (_, _) =>
        {
            CloseWith(CloseAction.None);
            _openDetailedSearch();
        };
        Grid.SetColumn(advancedButton, 1);
        footer.Children.Add(advancedButton);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
        return root;
    }

    private void OnSearchBoxKeyDown(object sender, KeyRoutedEventArgs args)
    {
        switch (args.Key)
        {
            case VirtualKey.Down:
                MoveSelection(1);
                args.Handled = true;
                break;
            case VirtualKey.Up:
                MoveSelection(-1);
                args.Handled = true;
                break;
            case VirtualKey.PageDown:
                MoveSelection(10);
                args.Handled = true;
                break;
            case VirtualKey.PageUp:
                MoveSelection(-10);
                args.Handled = true;
                break;
            case VirtualKey.Enter:
                args.Handled = true;
                InvokeSelected(asPlainText: IsControlDown());
                break;
            case VirtualKey.Escape:
                args.Handled = true;
                CloseWith(CloseAction.ReturnToMenu);
                break;
        }
    }

    private static bool IsControlDown()
    {
        return (NativeMethods.GetAsyncKeyState(NativeMethods.VkControl) & 0x8000) != 0;
    }

    private void MoveSelection(int delta)
    {
        var count = _resultsList.Items.Count;
        if (count == 0)
        {
            return;
        }

        var index = _resultsList.SelectedIndex;
        index = index < 0
            ? delta > 0 ? 0 : count - 1
            : Math.Abs(delta) == 1
                ? (index + delta + count) % count
                : Math.Clamp(index + delta, 0, count - 1);
        _resultsList.SelectedIndex = index;
        _resultsList.ScrollIntoView(_resultsList.Items[index]);
    }

    private void InvokeSelected(bool asPlainText)
    {
        var selected = _resultsList.SelectedItem as ListViewItem
            ?? _resultsList.Items.OfType<ListViewItem>().FirstOrDefault();
        if (selected is { Tag: QuickMenuItem item })
        {
            InvokeItem(item, asPlainText);
        }
    }

    private void InvokeItem(QuickMenuItem item, bool asPlainText)
    {
        if (!item.IsEnabled)
        {
            return;
        }

        // Falling back to the normal paste keeps the window from closing into a
        // dead state when the item has no plain-text representation.
        var plain = asPlainText && item.PlainTextInvoke is not null;
        CloseWith(CloseAction.None);
        _invokeItem(item, plain);
    }

    private async void OnQueryChanged()
    {
        try
        {
            var version = ++_queryVersion;
            await Task.Delay(DebounceMilliseconds);
            if (version != _queryVersion || _closing)
            {
                return;
            }

            await RefreshResultsAsync(version);
        }
        catch (Exception exception)
        {
            AppDiagnostics.Log(exception, "Quick menu incremental search");
        }
    }

    private async void RefreshResults()
    {
        try
        {
            await RefreshResultsAsync(++_queryVersion);
        }
        catch (Exception exception)
        {
            AppDiagnostics.Log(exception, "Quick menu incremental search");
        }
    }

    private async Task RefreshResultsAsync(long version)
    {
        var query = _searchBox.Text.Trim();
        var source = _searchSource;
        var results = await Task.Run(() =>
        {
            var filter = SearchFilter.Parse(query);
            var matches = filter.IsEmpty
                ? source
                : source.Where(item => QuickMenuWindow.MatchesSearch(filter, item));
            return matches.Take(MaxResults).ToArray();
        });

        if (version != _queryVersion || _closing)
        {
            return;
        }

        RenderResults(results);
    }

    private void RenderResults(IReadOnlyList<QuickMenuItem> results)
    {
        _resultsList.Items.Clear();
        foreach (var item in results)
        {
            _resultsList.Items.Add(GetOrCreateRow(item));
        }

        _emptyText.Visibility = results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (results.Count > 0)
        {
            _resultsList.SelectedIndex = 0;
        }
    }

    private ListViewItem GetOrCreateRow(QuickMenuItem item)
    {
        if (_rowCache.TryGetValue(item, out var cached))
        {
            return cached;
        }

        var row = new Grid { Padding = new Thickness(4, 7, 4, 7) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        row.Children.Add(CreateRowIcon(item));

        var textPanel = new StackPanel
        {
            Spacing = 1,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 8, 0)
        };
        textPanel.Children.Add(new TextBlock
        {
            Text = NormalizeSingleLine(item.Title),
            FontSize = 14,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        var subtitle = BuildSubtitle(item);
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            textPanel.Children.Add(new TextBlock
            {
                Text = subtitle,
                FontSize = 11.5,
                Opacity = 0.6,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
        }

        Grid.SetColumn(textPanel, 1);
        row.Children.Add(textPanel);

        if (item.CapturedAt is { } capturedAt)
        {
            var time = new TextBlock
            {
                Text = capturedAt.LocalDateTime.ToString("MM/dd HH:mm"),
                FontSize = 11.5,
                Opacity = 0.5,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(time, 2);
            row.Children.Add(time);
        }

        var listItem = new ListViewItem
        {
            Content = row,
            Tag = item,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(6, 0, 6, 0),
            MinHeight = 48
        };
        _rowCache[item] = listItem;
        return listItem;
    }

    private FrameworkElement CreateRowIcon(QuickMenuItem item)
    {
        if (item.IconImageBytes is { Length: > 0 } imageBytes)
        {
            var image = new Image { Stretch = Stretch.UniformToFill };
            var thumbnail = new Border
            {
                Width = 46,
                Height = 32,
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(60, 128, 128, 128)),
                Background = new SolidColorBrush(Color.FromArgb(24, 128, 128, 128)),
                VerticalAlignment = VerticalAlignment.Center,
                Child = image
            };
            LoadThumbnail(image, imageBytes);
            return thumbnail;
        }

        return new FontIcon
        {
            Glyph = string.IsNullOrWhiteSpace(item.IconGlyph) ? "" : item.IconGlyph,
            FontFamily = new FontFamily(item.IconFontFamily ?? "Segoe Fluent Icons"),
            FontSize = 15,
            Opacity = 0.75,
            Width = 46,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private async void LoadThumbnail(Image image, byte[] imageBytes)
    {
        try
        {
            image.Source = await QuickMenuWindow.CreateBitmapImageAsync(imageBytes, decodePixelWidth: 96);
        }
        catch (Exception exception)
        {
            AppDiagnostics.Log(exception, "Quick menu search thumbnail");
        }
    }

    private static string BuildSubtitle(QuickMenuItem item)
    {
        var subtitle = NormalizeSingleLine(item.Subtitle);
        var kind = NormalizeSingleLine(item.KindLabel);
        if (string.IsNullOrWhiteSpace(subtitle))
        {
            return kind;
        }

        return string.IsNullOrWhiteSpace(kind) || string.Equals(subtitle, kind, StringComparison.Ordinal)
            ? subtitle
            : $"{kind} - {subtitle}";
    }

    private static string NormalizeSingleLine(string? text)
    {
        return string.IsNullOrWhiteSpace(text)
            ? string.Empty
            : string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private void PositionNearCursor()
    {
        var id = Win32Interop.GetWindowIdFromWindow(_hwnd);
        if (AppWindow.GetFromWindowId(id) is not { } appWindow)
        {
            return;
        }

        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }

        var point = QuickMenuWindow.GetCursorPoint();
        var workingArea = QuickMenuWindow.GetWorkingArea(point);
        var x = Math.Clamp(point.X, workingArea.Left + 8, Math.Max(workingArea.Left + 8, workingArea.Right - WindowWidth - 8));
        var y = Math.Clamp(point.Y, workingArea.Top + 8, Math.Max(workingArea.Top + 8, workingArea.Bottom - WindowHeight - 8));
        appWindow.Resize(new SizeInt32(WindowWidth, WindowHeight));
        appWindow.Move(new PointInt32(x, y));
    }
}
