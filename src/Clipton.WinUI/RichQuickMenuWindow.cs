using Clipton.Core;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Runtime.InteropServices;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI;
using WinRT.Interop;

namespace Clipton.WinUI;

internal sealed class RichQuickMenuWindow : Window, IQuickMenuHostWindow
{
    private enum HeaderFilter
    {
        All,
        Pinned,
        Text,
        Image
    }

    private const int ScreenEdgePadding = 8;
    private const int MenuWidth = 392;
    private const int PreviewWidth = 320;
    private const int WindowHeight = 526;
    private const int WindowGap = 10;
    private const int DwmwaNcRenderingPolicy = 2;
    private const int DwmwaBorderColor = 34;
    private const int DwmncrpDisabled = 1;
    private const int DwmwaColorNone = unchecked((int)0xFFFFFFFE);
    private const int GwlStyle = -16;
    private const int VkPageUp = 0x21;
    private const int VkPageDown = 0x22;
    private const int VkEnd = 0x23;
    private const int VkHome = 0x24;
    private const long WsCaption = 0x00C00000L;
    private const long WsThickFrame = 0x00040000L;
    private const long WsBorder = 0x00800000L;
    private const long WsDlgFrame = 0x00400000L;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private const string PasteOptionsButtonTag = "RichPasteOptionsButton";
    private static readonly NativeMethods.LowLevelKeyboardProc s_keyboardProc = OnStaticKeyboardHook;
    private static IntPtr s_keyboardHook;
    private static RichQuickMenuWindow? s_activeWindow;
    private readonly string _title;
    private readonly string _searchPlaceholder;
    private readonly string _theme;
    private readonly QuickMenuShortcutSettings _shortcuts;
    private readonly Action _openHistory;
    private readonly string _showAllHistoryText;
    private readonly string _previewImageText;
    private readonly string _copyFeedbackText;
    private readonly string _cutFeedbackText;
    private readonly Grid _root = new();
    private readonly Border _menuCard = new();
    private readonly StackPanel _itemHost = new() { Spacing = 6 };
    private readonly TextBox _searchBox = new();
    private readonly Dictionary<HeaderFilter, Button> _filterButtons = [];
    private readonly Border _previewCard = new();
    private readonly Image _previewImage = new() { Stretch = Stretch.UniformToFill };
    private readonly TextBlock _previewMetaText = Text(12, 0.76);
    private readonly Border _feedbackPill = new();
    private readonly TextBlock _feedbackText = Text(12, 0.94);
    private readonly Border _loadingOverlay = new();
    private readonly ProgressRing _loadingRing = new() { Width = 22, Height = 22, IsActive = false };
    private readonly TextBlock _loadingText = Text("Loading...", 13, 0.92, Microsoft.UI.Text.FontWeights.SemiBold);
    private readonly DispatcherTimer _feedbackTimer = new() { Interval = TimeSpan.FromMilliseconds(900) };
    private readonly Stack<(IReadOnlyList<QuickMenuItem> Items, int SelectedIndex, HeaderFilter Filter)> _navigationStack = new();
    private IReadOnlyList<QuickMenuItem> _items = [];
    private readonly List<QuickMenuItem> _visibleItems = [];
    private readonly List<Border> _itemCards = [];
    private ScrollViewer? _scrollViewer;
    private Button? _backButton;
    private Button? _searchButton;
    private int _selectedIndex;
    private HeaderFilter _activeFilter = HeaderFilter.All;
    private AppWindow? _appWindow;
    private IntPtr _hwnd;
    private Window? _previewWindow;
    private AppWindow? _previewAppWindow;
    private IntPtr _previewHwnd;
    private NativeMethods.Point _anchorPoint;
    private NativeMethods.Point _dragStartCursor;
    private PointInt32 _dragStartWindowPosition;
    private bool _hasAnchorPoint;
    private bool _dismissed;
    private bool _dragging;
    private bool _previewVisible;
    private bool _folderLoading;
    private bool _searchBoxFocused;
    private bool _startInSearchMode;
    private IReadOnlyList<QuickMenuItem> _rootItemsForSearch = [];
    private QuickMenuItem[]? _searchResults;
    private Task<QuickMenuItem[]>? _searchSourceTask;
    private long _searchVersion;
    private long _previewRequestId;
    private long _focusRequestId;
    private long _folderLoadRequestId;

    public RichQuickMenuWindow(
        string title,
        IReadOnlyList<QuickMenuItem> items,
        string theme,
        QuickMenuShortcutSettings shortcuts,
        Action openHistory,
        string showAllHistoryText,
        string previewImageText,
        string copyFeedbackText,
        string cutFeedbackText,
        string searchPlaceholder,
        bool startInSearchMode = false)
    {
        _title = title;
        _items = items;
        _rootItemsForSearch = items;
        _searchPlaceholder = searchPlaceholder;
        _startInSearchMode = startInSearchMode;
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
        InstallKeyboardHook();
        PositionNearCursor(resetAnchor: true);
        if (_startInSearchMode)
        {
            _startInSearchMode = false;
            ShowSearch();
        }

        FocusMenuNow();
        QueueFocusRetry();
    }

    public void Reopen(IReadOnlyList<QuickMenuItem> items)
    {
        _items = items;
        _rootItemsForSearch = items;
        _selectedIndex = 0;
        _activeFilter = HeaderFilter.All;
        _navigationStack.Clear();
        _hasAnchorPoint = false;
        _searchVersion++;
        _searchResults = null;
        _searchSourceTask = null;
        _searchBox.Visibility = Visibility.Collapsed;
        if (_searchBox.Text.Length > 0)
        {
            _searchBox.Text = string.Empty;
        }

        RebuildItems();
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
        HideFolderLoading();
        UninstallKeyboardHook();
        DispatcherQueue.TryEnqueue(() =>
        {
            _previewAppWindow?.Hide();
            _appWindow?.Hide();
            Dismissed?.Invoke(this, EventArgs.Empty);
        });
    }

    private void InitializeWindow()
    {
        ExtendsContentIntoTitleBar = true;
        Content = _root;
        _root.IsTabStop = true;
        _root.KeyDown += OnKeyDown;
        _root.Loaded += (_, _) =>
        {
            FocusMenuNow();
            QueueFocusRetry();
        };
        var escapeAccelerator = new KeyboardAccelerator { Key = VirtualKey.Escape };
        escapeAccelerator.Invoked += (_, args) =>
        {
            args.Handled = true;
            Dismiss();
        };
        _root.KeyboardAccelerators.Add(escapeAccelerator);
        _root.Background = Brush(37, 37, 37);
        _hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        ConfigureWindowStyle();
        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.SetBorderAndTitleBar(false, false);
        }

        _appWindow.Resize(new SizeInt32(MenuWidth, WindowHeight));
        ApplyWindowRegion(_hwnd, MenuWidth, WindowHeight);
        DisableDwmBorder(_hwnd);
    }

    private void BuildContent()
    {
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(MenuWidth) });
        _root.Padding = new Thickness(0);

        _menuCard.Width = MenuWidth;
        _menuCard.Height = WindowHeight;
        _menuCard.CornerRadius = new CornerRadius(9);
        _menuCard.BorderThickness = new Thickness(0);
        _menuCard.Background = Brush(37, 37, 37);
        _menuCard.Padding = new Thickness(10);
        _menuCard.Child = BuildMenuPanel();
        _root.Children.Add(_menuCard);

        _root.Children.Add(new Border
        {
            Height = 2,
            Background = Brush(37, 37, 37),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false
        });

        _loadingRing.Foreground = Brush(245, 245, 245);
        var loadingPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        loadingPanel.Children.Add(_loadingRing);
        loadingPanel.Children.Add(_loadingText);
        _loadingOverlay.Width = MenuWidth;
        _loadingOverlay.Height = WindowHeight;
        _loadingOverlay.Background = new SolidColorBrush(Color.FromArgb(188, 24, 24, 24));
        _loadingOverlay.CornerRadius = new CornerRadius(9);
        _loadingOverlay.Visibility = Visibility.Collapsed;
        _loadingOverlay.IsHitTestVisible = true;
        _loadingOverlay.Child = new Border
        {
            Background = Brush(43, 43, 43),
            BorderBrush = Brush(72, 72, 72),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(18, 13, 18, 13),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = loadingPanel
        };
        _root.Children.Add(_loadingOverlay);
    }

    private void EnsurePreviewWindow()
    {
        if (_previewWindow is not null)
        {
            return;
        }

        _previewWindow = new Window { ExtendsContentIntoTitleBar = true };
        _previewCard.Width = PreviewWidth;
        _previewCard.Height = 338;
        _previewCard.CornerRadius = new CornerRadius(9);
        _previewCard.BorderThickness = new Thickness(0);
        _previewCard.Background = Brush(44, 44, 44);
        _previewCard.Padding = new Thickness(10);
        _previewCard.Visibility = Visibility.Collapsed;
        _previewCard.VerticalAlignment = VerticalAlignment.Center;
        _previewCard.IsTabStop = true;
        _previewCard.KeyDown += OnKeyDown;
        _previewCard.Child = BuildPreviewPanel();
        _previewWindow.Content = _previewCard;

        _previewHwnd = WindowNative.GetWindowHandle(_previewWindow);
        var windowId = Win32Interop.GetWindowIdFromWindow(_previewHwnd);
        _previewAppWindow = AppWindow.GetFromWindowId(windowId);
        ConfigureToolWindowStyle(_previewHwnd);
        if (_previewAppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.SetBorderAndTitleBar(false, false);
        }

        _previewAppWindow.Resize(new SizeInt32(PreviewWidth, 338));
        ApplyWindowRegion(_previewHwnd, PreviewWidth, 338);
        DisableDwmBorder(_previewHwnd);

        var escapeAccelerator = new KeyboardAccelerator { Key = VirtualKey.Escape };
        escapeAccelerator.Invoked += (_, args) =>
        {
            args.Handled = true;
            Dismiss();
        };
        _previewCard.KeyboardAccelerators.Add(escapeAccelerator);
    }

    private UIElement BuildMenuPanel()
    {
        var panel = new Grid();
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var toolbar = new Grid
        {
            Height = 42,
            Margin = new Thickness(0, 0, 0, 8),
            Background = new SolidColorBrush(Colors.Transparent)
        };
        toolbar.PointerPressed += OnHeaderPointerPressed;
        toolbar.PointerMoved += OnHeaderPointerMoved;
        toolbar.PointerReleased += OnHeaderPointerReleased;
        toolbar.PointerCanceled += OnHeaderPointerReleased;
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        AddToolbarButton(toolbar, 0, "\uE8C8", _title, HeaderFilter.All);
        AddToolbarButton(toolbar, 1, "\uE718", "Pinned", HeaderFilter.Pinned);
        AddToolbarButton(toolbar, 2, "\uE8D2", "Text", HeaderFilter.Text);
        AddToolbarButton(toolbar, 3, "\uEB9F", _previewImageText, HeaderFilter.Image);
        _searchButton = IconButton("\uE721", _searchPlaceholder);
        _searchButton.Margin = new Thickness(2, 0, 8, 0);
        _searchButton.Click += (_, _) => ToggleSearch();
        UpdateToolbarButton(_searchButton, selected: false);
        Grid.SetColumn(_searchButton, 4);
        toolbar.Children.Add(_searchButton);
        _backButton = IconButton("\uE72B", "Back");
        _backButton.Margin = new Thickness(2, 0, 8, 0);
        _backButton.HorizontalAlignment = HorizontalAlignment.Right;
        _backButton.Click += (_, _) => NavigateBack();
        Grid.SetColumn(_backButton, 5);
        toolbar.Children.Add(_backButton);
        AddToolbarButton(toolbar, 6, "\uE711", "Close", selected: false, Dismiss);
        panel.Children.Add(toolbar);

        _searchBox.PlaceholderText = _searchPlaceholder;
        _searchBox.Height = 34;
        _searchBox.Margin = new Thickness(0, 0, 0, 8);
        _searchBox.VerticalContentAlignment = VerticalAlignment.Center;
        _searchBox.Visibility = Visibility.Collapsed;
        _searchBox.GotFocus += (_, _) => _searchBoxFocused = true;
        _searchBox.LostFocus += (_, _) => _searchBoxFocused = false;
        _searchBox.TextChanged += (_, _) => ScheduleSearchUpdate();
        _searchBox.KeyDown += OnSearchBoxKeyDown;
        Grid.SetRow(_searchBox, 1);
        panel.Children.Add(_searchBox);

        _scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _itemHost
        };
        Grid.SetRow(_scrollViewer, 2);
        panel.Children.Add(_scrollViewer);

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
        Grid.SetRow(allHistoryButton, 3);
        panel.Children.Add(allHistoryButton);

        return panel;
    }

    private void ToggleSearch()
    {
        if (_searchBox.Visibility == Visibility.Visible)
        {
            HideSearch();
        }
        else
        {
            ShowSearch();
        }
    }

    private void ShowSearch()
    {
        // Warm up the flattened snapshot (folder materialization reads the
        // encrypted store) so the first keystroke filters without a long wait.
        _searchSourceTask ??= CreateSearchSourceTask(_rootItemsForSearch);
        _searchBox.Visibility = Visibility.Visible;
        if (_searchButton is not null)
        {
            UpdateToolbarButton(_searchButton, selected: true);
        }

        _searchBox.Focus(FocusState.Programmatic);
        _searchBox.Select(_searchBox.Text.Length, 0);
    }

    private void HideSearch()
    {
        _searchVersion++;
        _searchBox.Visibility = Visibility.Collapsed;
        if (_searchButton is not null)
        {
            UpdateToolbarButton(_searchButton, selected: false);
        }
        if (_searchBox.Text.Length > 0)
        {
            _searchBox.Text = string.Empty;
        }

        if (_searchResults is not null)
        {
            _searchResults = null;
            _selectedIndex = 0;
            RebuildItems();
        }

        _root.Focus(FocusState.Programmatic);
    }

    private void OnSearchBoxKeyDown(object sender, KeyRoutedEventArgs args)
    {
        switch (args.Key)
        {
            case VirtualKey.Down:
                args.Handled = true;
                MoveSelection(1);
                break;
            case VirtualKey.Up:
                args.Handled = true;
                MoveSelection(-1);
                break;
            case VirtualKey.Enter:
                args.Handled = true;
                InvokeSelected();
                break;
            case VirtualKey.Escape:
                args.Handled = true;
                HandleSearchEscape();
                break;
        }
    }

    private void HandleSearchEscape()
    {
        if (_searchBox.Text.Length > 0)
        {
            _searchBox.Text = string.Empty;
            return;
        }

        HideSearch();
    }

    private void ScheduleSearchUpdate()
    {
        var version = ++_searchVersion;
        _ = Task.Delay(150).ContinueWith(_ => DispatcherQueue.TryEnqueue(() =>
        {
            if (version == _searchVersion && !_dismissed)
            {
                ApplySearchQuery(version);
            }
        }));
    }

    private async void ApplySearchQuery(long version)
    {
        try
        {
            var query = _searchBox.Text.Trim();
            if (query.Length == 0)
            {
                if (_searchResults is not null)
                {
                    _searchResults = null;
                    _selectedIndex = 0;
                    RebuildItems();
                }

                return;
            }

            var sourceTask = _searchSourceTask ??= CreateSearchSourceTask(_rootItemsForSearch);
            var source = await sourceTask;
            var results = await Task.Run(() =>
            {
                var filter = SearchFilter.Parse(query);
                return source.Where(item => QuickMenuWindow.MatchesSearch(filter, item)).Take(60).ToArray();
            });

            if (version != _searchVersion || _dismissed)
            {
                return;
            }

            _searchResults = results;
            _selectedIndex = 0;
            RebuildItems();
            _scrollViewer?.ChangeView(null, 0, null, disableAnimation: true);
        }
        catch (Exception exception)
        {
            AppDiagnostics.Log(exception, "Rich quick menu search");
        }
    }

    private static Task<QuickMenuItem[]> CreateSearchSourceTask(IReadOnlyList<QuickMenuItem> rootItems)
    {
        return Task.Run(() => QuickMenuWindow.FlattenSearchableItems(rootItems).ToArray());
    }

    private void OnHeaderPointerPressed(object sender, PointerRoutedEventArgs args)
    {
        if (_appWindow is null || sender is not UIElement element || IsFromButton(args.OriginalSource))
        {
            return;
        }

        var point = args.GetCurrentPoint(element);
        if (!point.Properties.IsLeftButtonPressed || !NativeMethods.GetCursorPos(out var cursor))
        {
            return;
        }

        _dragging = true;
        _dragStartCursor = cursor;
        _dragStartWindowPosition = _appWindow.Position;
        element.CapturePointer(args.Pointer);
        args.Handled = true;
    }

    private void OnHeaderPointerMoved(object sender, PointerRoutedEventArgs args)
    {
        if (!_dragging || _appWindow is null || sender is not UIElement element)
        {
            return;
        }

        var point = args.GetCurrentPoint(element);
        if (!point.Properties.IsLeftButtonPressed || !NativeMethods.GetCursorPos(out var cursor))
        {
            EndHeaderDrag(element, args);
            return;
        }

        var x = _dragStartWindowPosition.X + cursor.X - _dragStartCursor.X;
        var y = _dragStartWindowPosition.Y + cursor.Y - _dragStartCursor.Y;
        _appWindow.Move(new PointInt32(x, y));
        ApplyWindowRegion(_hwnd, MenuWidth, WindowHeight);
        DisableDwmBorder(_hwnd);
        _anchorPoint = new NativeMethods.Point { X = x + 18, Y = y + 18 };
        _hasAnchorPoint = true;
        PositionPreviewWindow();
        args.Handled = true;
    }

    private void OnHeaderPointerReleased(object sender, PointerRoutedEventArgs args)
    {
        if (sender is UIElement element)
        {
            EndHeaderDrag(element, args);
        }
    }

    private void EndHeaderDrag(UIElement element, PointerRoutedEventArgs args)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        element.ReleasePointerCapture(args.Pointer);
        args.Handled = true;
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

        IEnumerable<QuickMenuItem> source = _searchResults ?? _items;
        foreach (var item in source.Where(item => !item.IsSeparator && MatchesFilter(item)))
        {
            _visibleItems.Add(item);
            var card = BuildItemCard(item, _visibleItems.Count - 1);
            _itemCards.Add(card);
            _itemHost.Children.Add(card);
        }

        if (_visibleItems.Count == 0)
        {
            _selectedIndex = 0;
            _scrollViewer?.ChangeView(null, 0, null, disableAnimation: true);
            _ = UpdatePreviewAsync(null);
            UpdateToolbarSelection();
            return;
        }

        _selectedIndex = Math.Clamp(_selectedIndex, 0, _visibleItems.Count - 1);
        UpdateToolbarSelection();
        UpdateSelection();
    }

    private bool MatchesFilter(QuickMenuItem item)
    {
        return _activeFilter switch
        {
            HeaderFilter.Pinned => item.IsPinned,
            HeaderFilter.Text => item.KindLabel.Equals("Text", StringComparison.OrdinalIgnoreCase)
                || item.KindLabel.Equals("Code", StringComparison.OrdinalIgnoreCase)
                || item.KindLabel.Equals("Link", StringComparison.OrdinalIgnoreCase),
            HeaderFilter.Image => IsImageItem(item),
            _ => true
        };
    }

    private void SetFilter(HeaderFilter filter)
    {
        if (_activeFilter == filter)
        {
            return;
        }

        _activeFilter = filter;
        _selectedIndex = 0;
        RebuildItems();
        _root.Focus(FocusState.Programmatic);
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
        card.Tapped += (_, args) =>
        {
            if (IsFromPasteOptionsButton(args.OriginalSource))
            {
                args.Handled = true;
                return;
            }

            InvokeItem(item);
        };
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

        var trailing = item.IsFolder ? CreateFolderGlyph()
            : item.PasteOptions is { Count: > 0 } ? CreatePasteOptionsButton(item)
            : CreatePinnedGlyph(item);
        Grid.SetColumn(trailing, 2);
        grid.Children.Add(trailing);

        return grid;
    }

    private FrameworkElement CreatePasteOptionsButton(QuickMenuItem item)
    {
        var button = IconButton("\uE712", "More options");
        button.Width = 34;
        button.Height = 34;
        button.Margin = new Thickness(0);
        button.Background = Brush(40, 40, 40);
        button.BorderBrush = Brush(40, 40, 40);
        button.Tag = PasteOptionsButtonTag;
        button.Tapped += (_, args) => args.Handled = true;
        button.Click += (_, _) => ShowPasteOptions(item, button);
        return button;
    }

    private static bool IsFromPasteOptionsButton(object source)
    {
        var current = source as DependencyObject;
        while (current is not null)
        {
            if (current is Button { Tag: PasteOptionsButtonTag })
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private static bool IsFromButton(object source)
    {
        var current = source as DependencyObject;
        while (current is not null)
        {
            if (current is Button)
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private static TextBlock CreatePinnedGlyph(QuickMenuItem item)
    {
        var trailing = Text("\uE734", 19, item.IsPinned ? 0.86 : 0.48);
        trailing.FontFamily = new FontFamily("Segoe Fluent Icons");
        trailing.VerticalAlignment = VerticalAlignment.Center;
        return trailing;
    }

    private static TextBlock CreateFolderGlyph()
    {
        var trailing = Text("\uE974", 18, 0.78);
        trailing.FontFamily = new FontFamily("Segoe Fluent Icons");
        trailing.VerticalAlignment = VerticalAlignment.Center;
        return trailing;
    }

    private void ShowPasteOptions(QuickMenuItem item, FrameworkElement anchor)
    {
        if (item.PasteOptions is not { Count: > 0 })
        {
            return;
        }

        var flyout = new MenuFlyout
        {
            Placement = FlyoutPlacementMode.RightEdgeAlignedTop
        };
        foreach (var option in item.PasteOptions)
        {
            flyout.Items.Add(CreatePasteOptionMenuItem(option));
        }

        flyout.ShowAt(anchor);
    }

    private MenuFlyoutItem CreatePasteOptionMenuItem(QuickMenuPasteOption option)
    {
        var optionItem = new MenuFlyoutItem
        {
            Text = option.Text,
            Icon = CreateOptionIcon(option),
            Tag = option
        };
        optionItem.Click += (_, _) => InvokePasteOption(option);
        return optionItem;
    }

    private UIElement CreateItemIcon(QuickMenuItem item)
    {
        if (item.IconImageBytes is { Length: > 0 } bytes)
        {
            var image = new Image { Stretch = Stretch.UniformToFill };
            _ = SetImageSourceAsync(image, bytes);
            return image;
        }

        var glyph = item.IsFolder && item.IconGlyph == ">" ? GetFallbackGlyph(item) : item.IconGlyph ?? GetFallbackGlyph(item);
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

        _itemCards.ElementAtOrDefault(_selectedIndex)?.StartBringIntoView(new BringIntoViewOptions
        {
            AnimationDesired = false,
            VerticalAlignmentRatio = 0.5
        });
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
        EnsurePreviewWindow();
        _previewCard.Visibility = Visibility.Visible;
        ResizeForPreview(showPreview: true);
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs args)
    {
        var key = args.Key;
        var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        if (_folderLoading)
        {
            args.Handled = true;
            if (key == VirtualKey.Escape)
            {
                Dismiss();
            }

            return;
        }

        switch (key)
        {
            case VirtualKey.Escape:
                args.Handled = true;
                Dismiss();
                break;
            case VirtualKey.Down:
                args.Handled = true;
                MoveSelection(1);
                break;
            case VirtualKey.Up:
                args.Handled = true;
                MoveSelection(-1);
                break;
            case VirtualKey.PageDown:
                args.Handled = true;
                MoveSelection(5);
                break;
            case VirtualKey.PageUp:
                args.Handled = true;
                MoveSelection(-5);
                break;
            case VirtualKey.Home:
                args.Handled = true;
                Select(0);
                break;
            case VirtualKey.End:
                args.Handled = true;
                Select(_visibleItems.Count - 1);
                break;
            case VirtualKey.Enter:
                args.Handled = true;
                InvokeSelected();
                break;
            case VirtualKey.Left:
            case VirtualKey.Back:
                args.Handled = true;
                NavigateBack();
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
                else if (MatchesShortcut(_shortcuts.Search, key, ctrl))
                {
                    args.Handled = true;
                    ToggleSearch();
                }

                break;
        }
    }

    private void MoveSelection(int delta)
    {
        if (_visibleItems.Count == 0)
        {
            return;
        }

        Select((_selectedIndex + delta + _visibleItems.Count) % _visibleItems.Count);
    }

    private void SelectFirst() => Select(0);

    private void SelectLast() => Select(_visibleItems.Count - 1);

    private void InstallKeyboardHook()
    {
        s_activeWindow = this;
        if (s_keyboardHook == IntPtr.Zero)
        {
            s_keyboardHook = NativeMethods.SetWindowsHookEx(
                NativeMethods.WhKeyboardLl,
                s_keyboardProc,
                NativeMethods.GetModuleHandle(null),
                0);
        }
    }

    private void UninstallKeyboardHook()
    {
        if (ReferenceEquals(s_activeWindow, this))
        {
            s_activeWindow = null;
        }

        if (s_keyboardHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(s_keyboardHook);
            s_keyboardHook = IntPtr.Zero;
        }
    }

    private static IntPtr OnStaticKeyboardHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        return s_activeWindow is { } active
            ? active.OnKeyboardHook(nCode, wParam, lParam)
            : NativeMethods.CallNextHookEx(s_keyboardHook, nCode, wParam, lParam);
    }

    private IntPtr OnKeyboardHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0 || (wParam.ToInt32() != NativeMethods.WmKeydown && wParam.ToInt32() != NativeMethods.WmSyskeydown))
        {
            return NativeMethods.CallNextHookEx(s_keyboardHook, nCode, wParam, lParam);
        }

        if (!IsInteractionForeground())
        {
            return NativeMethods.CallNextHookEx(s_keyboardHook, nCode, wParam, lParam);
        }

        var key = Marshal.ReadInt32(lParam);
        var ctrl = (NativeMethods.GetAsyncKeyState(NativeMethods.VkControl) & 0x8000) != 0;
        if (_folderLoading)
        {
            if (key == NativeMethods.VkEscape)
            {
                DispatcherQueue.TryEnqueue(Dismiss);
            }

            return 1;
        }

        // While the search box is editing, only list navigation is intercepted;
        // everything else (letters, Left/Right, Backspace, Home/End, Ctrl+C...)
        // must reach the text box.
        if (_searchBoxFocused)
        {
            switch (key)
            {
                case NativeMethods.VkDown:
                    DispatcherQueue.TryEnqueue(() => MoveSelection(1));
                    return 1;
                case NativeMethods.VkUp:
                    DispatcherQueue.TryEnqueue(() => MoveSelection(-1));
                    return 1;
                case VkPageDown:
                    DispatcherQueue.TryEnqueue(() => MoveSelection(5));
                    return 1;
                case VkPageUp:
                    DispatcherQueue.TryEnqueue(() => MoveSelection(-5));
                    return 1;
                case NativeMethods.VkReturn:
                    DispatcherQueue.TryEnqueue(InvokeSelected);
                    return 1;
                case NativeMethods.VkEscape:
                    DispatcherQueue.TryEnqueue(HandleSearchEscape);
                    return 1;
                default:
                    if (MatchesShortcut(key, ctrl, _shortcuts.Search))
                    {
                        DispatcherQueue.TryEnqueue(HideSearch);
                        return 1;
                    }

                    return NativeMethods.CallNextHookEx(s_keyboardHook, nCode, wParam, lParam);
            }
        }

        switch (key)
        {
            case NativeMethods.VkDown:
                DispatcherQueue.TryEnqueue(() => MoveSelection(1));
                return 1;
            case NativeMethods.VkUp:
                DispatcherQueue.TryEnqueue(() => MoveSelection(-1));
                return 1;
            case VkPageDown:
                DispatcherQueue.TryEnqueue(() => MoveSelection(5));
                return 1;
            case VkPageUp:
                DispatcherQueue.TryEnqueue(() => MoveSelection(-5));
                return 1;
            case VkHome:
                DispatcherQueue.TryEnqueue(SelectFirst);
                return 1;
            case VkEnd:
                DispatcherQueue.TryEnqueue(SelectLast);
                return 1;
            case NativeMethods.VkLeft:
            case NativeMethods.VkBack:
                DispatcherQueue.TryEnqueue(NavigateBack);
                return 1;
            case NativeMethods.VkReturn:
                DispatcherQueue.TryEnqueue(InvokeSelected);
                return 1;
            case NativeMethods.VkEscape:
                DispatcherQueue.TryEnqueue(Dismiss);
                return 1;
            case NativeMethods.VkC when ctrl:
                DispatcherQueue.TryEnqueue(CopySelectedImage);
                return 1;
            case NativeMethods.VkX when ctrl:
                DispatcherQueue.TryEnqueue(CutSelectedImage);
                return 1;
            default:
                if (MatchesShortcut(key, ctrl, _shortcuts.PastePlainText))
                {
                    DispatcherQueue.TryEnqueue(InvokeSelectedPlainText);
                    return 1;
                }

                if (MatchesShortcut(key, ctrl, _shortcuts.Search))
                {
                    DispatcherQueue.TryEnqueue(ToggleSearch);
                    return 1;
                }

                return NativeMethods.CallNextHookEx(s_keyboardHook, nCode, wParam, lParam);
        }
    }

    private bool IsInteractionForeground()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        return foreground == _hwnd || (_previewHwnd != IntPtr.Zero && foreground == _previewHwnd);
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

        if (_folderLoading || !item.IsEnabled)
        {
            return;
        }

        BeginInvokeAndDismiss(item.PlainTextInvoke);
    }

    private void InvokeItem(QuickMenuItem? item)
    {
        if (_folderLoading || item is null || !item.IsEnabled)
        {
            return;
        }

        if (item.IsFolder)
        {
            OpenFolder(item);
            return;
        }

        BeginInvokeAndDismiss(item.Invoke);
    }

    // Hide the windows and release the hook before pasting: invoking while the
    // menu is still foreground races the paste-target restore and the injected
    // Ctrl+V can land in the wrong window.
    private void BeginInvokeAndDismiss(Action action)
    {
        UninstallKeyboardHook();
        _previewAppWindow?.Hide();
        _appWindow?.Hide();
        _ = Task.Delay(90).ContinueWith(_ => DispatcherQueue.TryEnqueue(() =>
        {
            action();
            Dismiss();
        }));
    }

    private async void OpenFolder(QuickMenuItem item)
    {
        var requestId = Interlocked.Increment(ref _folderLoadRequestId);
        ShowFolderLoading(item.Title);
        IReadOnlyList<QuickMenuItem> children;
        try
        {
            await Task.Yield();
            children = await Task.Run(item.GetChildren);
        }
        catch (Exception exception)
        {
            AppDiagnostics.Log(exception, $"Rich quick menu folder load failed. folder={item.Title}");
            children = [];
        }

        DispatcherQueue.TryEnqueue(() => CompleteOpenFolder(requestId, children));
    }

    private void CompleteOpenFolder(long requestId, IReadOnlyList<QuickMenuItem> children)
    {
        if (_dismissed || requestId != _folderLoadRequestId)
        {
            return;
        }

        HideFolderLoading();
        if (children.Count == 0)
        {
            return;
        }

        _navigationStack.Push((_items, _selectedIndex, _activeFilter));
        _items = children;
        _selectedIndex = 0;
        _activeFilter = HeaderFilter.All;
        RebuildItems();
        _scrollViewer?.ChangeView(null, 0, null, disableAnimation: true);
        FocusMenuNow();
        QueueFocusRetry();
    }

    private void ShowFolderLoading(string title)
    {
        _folderLoading = true;
        _loadingText.Text = string.IsNullOrWhiteSpace(title) ? "Loading..." : $"Loading {TrimText(title, 18)}...";
        _loadingRing.IsActive = true;
        _loadingOverlay.Visibility = Visibility.Visible;
        _previewAppWindow?.Hide();
    }

    private void HideFolderLoading()
    {
        _folderLoading = false;
        _loadingRing.IsActive = false;
        _loadingOverlay.Visibility = Visibility.Collapsed;
    }

    private void NavigateBack()
    {
        if (_navigationStack.Count == 0)
        {
            return;
        }

        var previous = _navigationStack.Pop();
        _items = previous.Items;
        _selectedIndex = previous.SelectedIndex;
        _activeFilter = previous.Filter;
        RebuildItems();
        _itemCards.ElementAtOrDefault(_selectedIndex)?.StartBringIntoView(new BringIntoViewOptions
        {
            AnimationDesired = false,
            VerticalAlignmentRatio = 0.5
        });
        FocusMenuNow();
        QueueFocusRetry();
    }

    private void InvokePasteOption(QuickMenuPasteOption option)
    {
        BeginInvokeAndDismiss(option.Invoke);
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
        _previewVisible = showPreview;
        if (!showPreview)
        {
            _previewAppWindow?.Hide();
        }
        else
        {
            PositionPreviewWindow();
        }
    }

    private void PositionNearCursor(bool resetAnchor)
    {
        if (_appWindow is null)
        {
            return;
        }

        if (resetAnchor || !_hasAnchorPoint)
        {
            _anchorPoint = NativeMethods.GetCursorPos(out var point)
                ? point
                : new NativeMethods.Point { X = 200, Y = 200 };
            _hasAnchorPoint = true;
        }

        var workArea = GetWorkArea(_anchorPoint);
        var menuX = Math.Clamp(_anchorPoint.X - 18, workArea.Left + ScreenEdgePadding, workArea.Right - MenuWidth - ScreenEdgePadding);
        var y = Math.Clamp(_anchorPoint.Y - 18, workArea.Top + ScreenEdgePadding, workArea.Bottom - WindowHeight - ScreenEdgePadding);
        _appWindow.Resize(new SizeInt32(MenuWidth, WindowHeight));
        _appWindow.Move(new PointInt32(menuX, y));
        ApplyWindowRegion(_hwnd, MenuWidth, WindowHeight);
        DisableDwmBorder(_hwnd);
        PositionPreviewWindow();
        BringToFront();
    }

    private void PositionPreviewWindow()
    {
        if (!_previewVisible || _previewCard.Visibility != Visibility.Visible || _previewAppWindow is null || !_hasAnchorPoint)
        {
            return;
        }

        var workArea = GetWorkArea(_anchorPoint);
        var menuX = Math.Clamp(_anchorPoint.X - 18, workArea.Left + ScreenEdgePadding, workArea.Right - MenuWidth - ScreenEdgePadding);
        var menuY = Math.Clamp(_anchorPoint.Y - 18, workArea.Top + ScreenEdgePadding, workArea.Bottom - WindowHeight - ScreenEdgePadding);
        var rightX = menuX + MenuWidth + WindowGap;
        var leftX = menuX - PreviewWidth - WindowGap;
        var previewX = rightX + PreviewWidth <= workArea.Right - ScreenEdgePadding
            ? rightX
            : Math.Max(workArea.Left + ScreenEdgePadding, leftX);
        var previewY = Math.Clamp(menuY + 90, workArea.Top + ScreenEdgePadding, workArea.Bottom - 338 - ScreenEdgePadding);
        _previewAppWindow.Resize(new SizeInt32(PreviewWidth, 338));
        _previewAppWindow.Move(new PointInt32(previewX, previewY));
        ApplyWindowRegion(_previewHwnd, PreviewWidth, 338);
        _previewAppWindow.Show();
        DisableDwmBorder(_previewHwnd);
        BringPreviewToFront();
        FocusMenuNow();
        QueueFocusRetry();
    }

    private void FocusMenuNow()
    {
        Activate();
        BringToFront();
        if (_searchBox.Visibility == Visibility.Visible)
        {
            _searchBox.Focus(FocusState.Programmatic);
        }
        else
        {
            _root.Focus(FocusState.Programmatic);
        }
    }

    private void QueueFocusRetry()
    {
        var requestId = Interlocked.Increment(ref _focusRequestId);
        DispatcherQueue.TryEnqueue(() =>
        {
            _ = Task.Delay(60).ContinueWith(_ => DispatcherQueue.TryEnqueue(() =>
            {
                if (_dismissed || requestId != _focusRequestId)
                {
                    return;
                }

                FocusMenuNow();
            }));
        });
    }

    private void ConfigureWindowStyle()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        ConfigureToolWindowStyle(_hwnd);
    }

    private static void ConfigureToolWindowStyle(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlExstyle).ToInt64();
        exStyle |= NativeMethods.WsExLayered | NativeMethods.WsExToolwindow;
        exStyle &= ~NativeMethods.WsExAppwindow;
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GwlExstyle, new IntPtr(exStyle));
        NativeMethods.SetLayeredWindowAttributes(hwnd, 0, 255, NativeMethods.LwaAlpha);

        var style = NativeMethods.GetWindowLongPtr(hwnd, GwlStyle).ToInt64();
        style &= ~(WsCaption | WsThickFrame | WsBorder | WsDlgFrame);
        NativeMethods.SetWindowLongPtr(hwnd, GwlStyle, new IntPtr(style));
        _ = SetWindowPos(
            hwnd,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
    }

    private static void DisableDwmBorder(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var borderColor = DwmwaColorNone;
        _ = DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref borderColor, sizeof(int));

        var ncRenderingPolicy = DwmncrpDisabled;
        _ = DwmSetWindowAttribute(hwnd, DwmwaNcRenderingPolicy, ref ncRenderingPolicy, sizeof(int));
    }

    private static void ApplyWindowRegion(IntPtr hwnd, int width, int height)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var region = CreateRoundRectRgn(0, 1, width, height, 18, 18);
        if (region == IntPtr.Zero)
        {
            return;
        }

        if (SetWindowRgn(hwnd, region, true) == 0)
        {
            _ = DeleteObject(region);
        }
    }

    private void BringToFront()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.BringWindowToTop(_hwnd);
        NativeMethods.SetForegroundWindow(_hwnd);
        NativeMethods.SetActiveWindow(_hwnd);
        NativeMethods.SetFocus(_hwnd);
    }

    private void BringPreviewToFront()
    {
        if (_previewHwnd == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.BringWindowToTop(_previewHwnd);
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

    private void AddToolbarButton(Grid toolbar, int column, string glyph, string tooltip, HeaderFilter filter)
    {
        var button = IconButton(glyph, tooltip);
        button.Margin = new Thickness(2, 0, 8, 0);
        button.Click += (_, _) => SetFilter(filter);
        _filterButtons[filter] = button;
        Grid.SetColumn(button, column);
        toolbar.Children.Add(button);
        UpdateToolbarButton(button, filter == _activeFilter);
    }

    private void UpdateToolbarSelection()
    {
        foreach (var (filter, button) in _filterButtons)
        {
            UpdateToolbarButton(button, filter == _activeFilter);
        }

        if (_backButton is not null)
        {
            _backButton.Visibility = _navigationStack.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            _backButton.IsEnabled = _navigationStack.Count > 0;
        }
    }

    private static void UpdateToolbarButton(Button button, bool selected)
    {
        button.Background = selected ? Brush(36, 79, 102) : Brush(37, 37, 37);
        button.BorderBrush = selected ? Brush(23, 150, 214) : Brush(37, 37, 37);
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

    private static IconElement? CreateOptionIcon(QuickMenuPasteOption option)
    {
        if (string.IsNullOrWhiteSpace(option.IconGlyph))
        {
            return null;
        }

        return new FontIcon
        {
            Glyph = option.IconGlyph,
            FontFamily = new FontFamily(option.IconFontFamily ?? "Segoe Fluent Icons"),
            FontSize = option.IconFontFamily?.Equals("Segoe UI", StringComparison.OrdinalIgnoreCase) == true ? 13 : 12
        };
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

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int widthEllipse, int heightEllipse);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool redraw);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

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
        return item.IsFolder
            ? "\uE8B7"
            : item.KindLabel.Equals("Image", StringComparison.OrdinalIgnoreCase)
            ? "\uEB9F"
            : item.KindLabel.Equals("Link", StringComparison.OrdinalIgnoreCase)
                ? "\uE71B"
                : item.KindLabel.Equals("Code", StringComparison.OrdinalIgnoreCase)
                    ? "\uE943"
                    : "\uE8A5";
    }

    private static bool IsImageItem(QuickMenuItem item)
    {
        return item.KindLabel.Equals("Image", StringComparison.OrdinalIgnoreCase)
            || item.IconImageBytes is { Length: > 0 };
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

    private static bool MatchesShortcut(int key, bool ctrl, string shortcut)
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
            && (int)parsedKey == key;
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
