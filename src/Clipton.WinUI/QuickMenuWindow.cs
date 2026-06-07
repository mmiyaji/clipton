using System.Runtime.InteropServices;
using Clipton.Core;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI;
using WinRT.Interop;

namespace Clipton.WinUI;

public sealed class QuickMenuWindow : Window
{
    private const int MaxMenuLineLength = 34;
    private const int HostWindowSize = 1;
    private const int ScreenEdgePadding = 8;
    private const int EstimatedRootFlyoutHeight = 420;
    private const int NativeResourceCollectionMinIntervalMilliseconds = 5000;
    private static readonly NativeMethods.LowLevelKeyboardProc s_keyboardProc = OnStaticKeyboardHook;
    private static readonly NativeMethods.LowLevelMouseProc s_mouseProc = OnStaticMouseHook;
    private static QuickMenuWindow? s_activeWindow;
    private static IntPtr s_keyboardHook;
    private static IntPtr s_mouseHook;
    private static long s_lastNativeResourceCollectionTick;
    private QuickMenuNavigator? _navigator;
    private IReadOnlyList<QuickMenuItem> _rootItems = [];
    private readonly string _title;
    private readonly Grid _host = new();
    private readonly MenuFlyout _flyout = new();
    private readonly string _theme;
    private readonly string _searchTitle;
    private readonly string _searchPrompt;
    private readonly string _searchButtonText;
    private readonly string _advancedSearchButtonText;
    private readonly string _cancelButtonText;
    private readonly string _noSearchResultsText;
    private readonly string _imagePreviewSize;
    private readonly Action _openDetailedSearch;
    private readonly QuickMenuShortcutSettings _shortcuts;
    private readonly bool _showShortcutHints;
    private readonly List<MenuFlyoutItemBase> _rootFocusableItems = [];
    private readonly Dictionary<MenuFlyoutItemBase, List<MenuFlyoutItemBase>> _childFocusableItems = [];
    private readonly Dictionary<MenuFlyoutItemBase, MenuFlyoutItemBase> _parentItem = [];
    private readonly Dictionary<MenuFlyoutItemBase, MenuFlyout> _pasteOptionsFlyouts = [];
    private readonly HashSet<MenuFlyoutSubItem> _materializedFolderItems = [];
    private IReadOnlyList<MenuFlyoutItemBase> _activeFocusableItems = [];
    private MenuFlyoutItemBase? _activeParent;
    private int _focusedIndex = -1;
    private AppWindow? _appWindow;
    private IntPtr _hwnd;
    private bool _dismissed;
    private bool _opened;
    private int _lastNavigationKey;
    private long _lastNavigationTick;
    private long _focusToken;
    private IReadOnlyList<QuickMenuItem> _currentItems;
    private bool _rebuildingFlyout;
    private bool _revealMaskedItems;
    private bool _showCapturedAt;
    private MenuFlyoutItemBase? _hoveredPasteOptionsItem;
    private QuickMenuWindow? _replacementWindow;

    public QuickMenuWindow(
        string title,
        IReadOnlyList<QuickMenuItem> items,
        string theme,
        string imagePreviewSize,
        bool showCapturedAt,
        bool showShortcutHints,
        QuickMenuShortcutSettings shortcuts,
        Action openDetailedSearch,
        string searchTitle,
        string searchPrompt,
        string searchButtonText,
        string advancedSearchButtonText,
        string cancelButtonText,
        string noSearchResultsText)
    {
        _title = title;
        _navigator = new QuickMenuNavigator(title, items);
        _rootItems = items;
        _currentItems = items;
        _theme = theme;
        _imagePreviewSize = NormalizeImagePreviewSize(imagePreviewSize);
        _showCapturedAt = showCapturedAt;
        _showShortcutHints = showShortcutHints;
        _shortcuts = shortcuts;
        _openDetailedSearch = openDetailedSearch;
        _searchTitle = searchTitle;
        _searchPrompt = searchPrompt;
        _searchButtonText = searchButtonText;
        _advancedSearchButtonText = advancedSearchButtonText;
        _cancelButtonText = cancelButtonText;
        _noSearchResultsText = noSearchResultsText;
        Title = "Clipton";
        BuildHost();
        BuildFlyout();
        PositionNearCursor();
        _flyout.Closed += (_, _) =>
        {
            if (!_rebuildingFlyout)
            {
                Dismiss();
            }
        };
    }

    public event EventHandler? Dismissed;

    public void FocusMenu()
    {
        Activate();
        FocusHostWindow();
        DispatcherQueue.TryEnqueue(() =>
        {
            FocusHostWindow();
            ShowFlyout();
        });
    }

    public void Reopen(IReadOnlyList<QuickMenuItem> items)
    {
        UninstallKeyboardHook();
        UninstallMouseHook();
        _flyout.Hide();
        _appWindow?.Hide();
        ReleaseMenuReferences();
        _dismissed = false;
        _opened = false;
        _rootItems = items;
        _currentItems = items;
        _navigator = new QuickMenuNavigator(_title, items);
        BuildFlyout();
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
        UninstallKeyboardHook();
        UninstallMouseHook();
        DispatcherQueue.TryEnqueue(() =>
        {
            _flyout.Hide();
            _appWindow?.Hide();
            Dismissed?.Invoke(this, EventArgs.Empty);
            ReleaseMenuReferences();
            RequestNativeResourceCollection();
        });
    }

    private void ReleaseMenuReferences()
    {
        _flyout.Items.Clear();
        foreach (var flyout in _pasteOptionsFlyouts.Values)
        {
            flyout.Items.Clear();
        }

        _rootFocusableItems.Clear();
        _childFocusableItems.Clear();
        _parentItem.Clear();
        _pasteOptionsFlyouts.Clear();
        _materializedFolderItems.Clear();
        _activeFocusableItems = [];
        _activeParent = null;
        _hoveredPasteOptionsItem = null;
        _currentItems = [];
        _rootItems = [];
        _navigator = null;
        _replacementWindow = null;
    }

    private static void RequestNativeResourceCollection()
    {
        var now = Environment.TickCount64;
        var last = Volatile.Read(ref s_lastNativeResourceCollectionTick);
        if (now - last < NativeResourceCollectionMinIntervalMilliseconds)
        {
            return;
        }

        Interlocked.Exchange(ref s_lastNativeResourceCollectionTick, now);
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
    }

    private void BuildHost()
    {
        var dark = string.Equals(_theme, "dark", StringComparison.OrdinalIgnoreCase);
        _host.Background = new SolidColorBrush(dark ? Colors.Transparent : Colors.Transparent);
        _host.IsTabStop = true;
        _host.Loaded += (_, _) => ShowFlyout();
        Content = _host;
    }

    private void ShowFlyout()
    {
        if (_opened || _dismissed || _host.XamlRoot is null)
        {
            return;
        }

        _opened = true;
        InstallKeyboardHook();
        InstallMouseHook();
        FocusHostWindow();
        _flyout.ShowAt(_host, new FlyoutShowOptions
        {
            Placement = _flyout.Placement,
            Position = new Windows.Foundation.Point(0, 0)
        });
        var focusToken = ++_focusToken;
        DispatcherQueue.TryEnqueue(() =>
        {
            _ = Task.Delay(180).ContinueWith(_ => DispatcherQueue.TryEnqueue(() =>
            {
                if (_dismissed || _focusToken != focusToken)
                {
                    return;
                }

                FocusHostWindow();
                FocusMenuItem(0);
            }));
        });
    }

    private void FocusHostWindow()
    {
        if (_hwnd != IntPtr.Zero)
        {
            NativeMethods.SetForegroundWindow(_hwnd);
        }

        _host.Focus(FocusState.Programmatic);
    }

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
    }

    private void InstallMouseHook()
    {
        s_activeWindow = this;
        if (s_mouseHook == IntPtr.Zero)
        {
            s_mouseHook = NativeMethods.SetWindowsHookEx(
                NativeMethods.WhMouseLl,
                s_mouseProc,
                NativeMethods.GetModuleHandle(null),
                0);
        }
    }

    private void UninstallMouseHook()
    {
        if (ReferenceEquals(s_activeWindow, this))
        {
            s_activeWindow = null;
        }
    }

    private static IntPtr OnStaticMouseHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        return s_activeWindow is { } active
            ? active.OnMouseHook(nCode, wParam, lParam)
            : NativeMethods.CallNextHookEx(s_mouseHook, nCode, wParam, lParam);
    }

    private static IntPtr OnStaticKeyboardHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        return s_activeWindow is { } active
            ? active.OnKeyboardHook(nCode, wParam, lParam)
            : NativeMethods.CallNextHookEx(s_keyboardHook, nCode, wParam, lParam);
    }

    private IntPtr OnMouseHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0 || _host.XamlRoot is null)
        {
            return NativeMethods.CallNextHookEx(s_mouseHook, nCode, wParam, lParam);
        }

        var message = wParam.ToInt32();
        if (message is not NativeMethods.WmRbuttondown and not NativeMethods.WmRbuttonup)
        {
            return NativeMethods.CallNextHookEx(s_mouseHook, nCode, wParam, lParam);
        }

        var focusedElement = FocusManager.GetFocusedElement(_host.XamlRoot);
        var targetItem = _hoveredPasteOptionsItem ?? focusedElement as MenuFlyoutItemBase;
        if (targetItem is not { Tag: QuickMenuItem { PasteOptions.Count: > 0 } item } flyoutItem)
        {
            return NativeMethods.CallNextHookEx(s_mouseHook, nCode, wParam, lParam);
        }

        if (message == NativeMethods.WmRbuttonup)
        {
            DispatcherQueue.TryEnqueue(() => ShowPasteOptionsForItem(flyoutItem, EnsurePasteOptionsFlyout(flyoutItem, item), focusOptions: false));
        }

        return 1;
    }

    private IntPtr OnKeyboardHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0 || (wParam.ToInt32() != NativeMethods.WmKeydown && wParam.ToInt32() != NativeMethods.WmSyskeydown))
        {
            return NativeMethods.CallNextHookEx(s_keyboardHook, nCode, wParam, lParam);
        }

        var handled = true;
        var key = Marshal.ReadInt32(lParam);
        var controlDown = (NativeMethods.GetAsyncKeyState(NativeMethods.VkControl) & 0x8000) != 0;
        if (MatchesShortcut(key, controlDown, _shortcuts.Search))
        {
            DispatcherQueue.TryEnqueue(SearchMenu);
            return 1;
        }

        if (MatchesShortcut(key, controlDown, _shortcuts.ToggleMaskReveal))
        {
            DispatcherQueue.TryEnqueue(() => ToggleDisplayMode(revealMaskedItems: !_revealMaskedItems));
            return 1;
        }

        if (MatchesShortcut(key, controlDown, _shortcuts.ToggleCapturedAt))
        {
            DispatcherQueue.TryEnqueue(() => ToggleDisplayMode(showCapturedAt: !_showCapturedAt));
            return 1;
        }

        if (MatchesShortcut(key, controlDown, _shortcuts.PastePlainText))
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (GetFocusedMenuItem() is { } item)
                {
                    Invoke(item, asPlainText: true);
                }
            });
            return 1;
        }

        switch (key)
        {
            case NativeMethods.VkDown:
                if (ShouldHandleNavigationKey(key))
                {
                    DispatcherQueue.TryEnqueue(() => MoveFocus(1));
                }
                break;
            case NativeMethods.VkUp:
                if (ShouldHandleNavigationKey(key))
                {
                    DispatcherQueue.TryEnqueue(() => MoveFocus(-1));
                }
                break;
            case NativeMethods.VkLeft:
                if (_activeParent is MenuFlyoutSubItem)
                {
                    DispatcherQueue.TryEnqueue(() => EnqueueAfterDelay(80, ReturnToParentFocusContext));
                    handled = false;
                }
                else if (_activeParent is not null)
                {
                    DispatcherQueue.TryEnqueue(HandleLeftNavigation);
                }
                else if (GetLeftNavigationTarget() is MenuFlyoutSubItem)
                {
                    handled = false;
                }
                else
                {
                    handled = false;
                }
                break;
            case NativeMethods.VkRight:
                if (GetFocusedMenuItemBase() is MenuFlyoutSubItem folderItem)
                {
                    DispatcherQueue.TryEnqueue(() => PrepareFolderSubmenuForNativeOpen(folderItem));
                    handled = false;
                }
                else if (GetFocusedMenuItemBase() is { } focusedMenuItem
                    && focusedMenuItem.Tag is QuickMenuItem { PasteOptions.Count: > 0 })
                {
                    DispatcherQueue.TryEnqueue(EnterChildFocusContext);
                }
                break;
            case NativeMethods.VkReturn:
                if (GetFocusedMenuItemBase() is MenuFlyoutSubItem returnFolderItem)
                {
                    DispatcherQueue.TryEnqueue(() => PrepareFolderSubmenuForNativeOpen(returnFolderItem));
                    handled = false;
                    break;
                }

                DispatcherQueue.TryEnqueue(() =>
                {
                    if (GetFocusedPasteOption() is { } option)
                    {
                        InvokePasteOption(option);
                    }
                    else if (GetFocusedMenuItem() is { } item)
                    {
                        Invoke(item, asPlainText: false);
                    }
                });
                break;
            case NativeMethods.VkEscape:
                Dismiss();
                break;
            default:
                handled = false;
                break;
        }

        return handled
            ? 1
            : NativeMethods.CallNextHookEx(s_keyboardHook, nCode, wParam, lParam);
    }

    private static bool MatchesShortcut(int key, bool controlDown, string shortcut)
    {
        if (string.IsNullOrWhiteSpace(shortcut))
        {
            return false;
        }

        var parts = shortcut.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        var requiresControl = parts.Take(parts.Length - 1).Any(part => part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)
            || part.Equals("Control", StringComparison.OrdinalIgnoreCase));
        if (requiresControl != controlDown)
        {
            return false;
        }

        var keyName = parts[^1];
        return keyName.Length == 1 && char.IsLetterOrDigit(keyName[0])
            ? key == char.ToUpperInvariant(keyName[0])
            : keyName.Equals("Enter", StringComparison.OrdinalIgnoreCase) && key == NativeMethods.VkReturn
                || keyName.Equals("Esc", StringComparison.OrdinalIgnoreCase) && key == NativeMethods.VkEscape;
    }

    private bool ShouldHandleNavigationKey(int key)
    {
        var now = Environment.TickCount64;
        if (_lastNavigationKey == key && now - _lastNavigationTick < 120)
        {
            return false;
        }

        _lastNavigationKey = key;
        _lastNavigationTick = now;
        return true;
    }

    private void ToggleDisplayMode(bool? revealMaskedItems = null, bool? showCapturedAt = null)
    {
        _revealMaskedItems = revealMaskedItems ?? _revealMaskedItems;
        _showCapturedAt = showCapturedAt ?? _showCapturedAt;
        RefreshVisibleItems(_flyout.Items);
    }

    private void RefreshVisibleItems(IList<MenuFlyoutItemBase> items)
    {
        foreach (var element in items)
        {
            switch (element)
            {
                case MenuFlyoutSubItem subItem:
                    if (subItem.Tag is QuickMenuItem folder)
                    {
                        subItem.Text = BuildDisplayText(folder);
                    }

                    RefreshVisibleItems(subItem.Items);
                    break;
                case MenuFlyoutItem flyoutItem:
                    if (flyoutItem.Tag is QuickMenuItem item)
                    {
                        flyoutItem.Text = BuildDisplayText(item);
                        flyoutItem.KeyboardAcceleratorTextOverride = BuildCommandHint(item);
                    }

                    break;
            }
        }
    }

    private void BuildFlyout()
    {
        _flyout.Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft;
        _flyout.Items.Clear();
        _rootFocusableItems.Clear();
        _childFocusableItems.Clear();
        _parentItem.Clear();
        _pasteOptionsFlyouts.Clear();
        _materializedFolderItems.Clear();
        _activeFocusableItems = _rootFocusableItems;
        _activeParent = null;
        _focusedIndex = -1;
        AddItems(_flyout.Items, _currentItems, parent: null);
    }

    private void EnqueueAfterDelay(int milliseconds, Action action)
    {
        _ = Task.Delay(milliseconds).ContinueWith(_ => DispatcherQueue.TryEnqueue(() =>
        {
            if (!_dismissed)
            {
                action();
            }
        }));
    }

    private async void SearchMenu()
    {
        _rebuildingFlyout = true;
        _flyout.Hide();
        UninstallKeyboardHook();
        UninstallMouseHook();
        var searchResult = await PromptForSearchAsync();
        if (searchResult?.OpenDetailedSearch == true)
        {
            Dismiss();
            _openDetailedSearch();
            return;
        }

        if (searchResult?.Query is null)
        {
            _opened = false;
            BuildFlyout();
            ShowFlyout();
            EnqueueAfterDelay(300, () => _rebuildingFlyout = false);
            return;
        }

        var normalizedQuery = searchResult.Query.Trim();
        var resultItems = string.IsNullOrWhiteSpace(normalizedQuery)
            ? _rootItems
            : FlattenSearchableItems(_rootItems)
                .Where(item => MatchesSearch(item, normalizedQuery))
                .Take(50)
                .ToArray();

        if (resultItems.Count == 0)
        {
            resultItems =
            [
                new QuickMenuItem(
                    string.Format(_noSearchResultsText, normalizedQuery),
                    _shortcuts.Search,
                    "-",
                    string.Empty,
                    () => { },
                    IsEnabled: false)
            ];
        }

        ShowReplacementMenu(resultItems);
    }

    private void ShowReplacementMenu(IReadOnlyList<QuickMenuItem> items)
    {
        _flyout.Hide();
        _appWindow?.Hide();
        UninstallKeyboardHook();
        UninstallMouseHook();
        _replacementWindow = new QuickMenuWindow(
            _searchTitle,
            items,
            _theme,
            _imagePreviewSize,
            _showCapturedAt,
            _showShortcutHints,
            _shortcuts,
            _openDetailedSearch,
            _searchTitle,
            _searchPrompt,
            _searchButtonText,
            _advancedSearchButtonText,
            _cancelButtonText,
            _noSearchResultsText);
        _replacementWindow.Dismissed += (_, _) => Dismiss();
        _replacementWindow.FocusMenu();
    }

    private async Task<SearchPromptResult?> PromptForSearchAsync()
    {
        var result = new TaskCompletionSource<SearchPromptResult?>();
        var window = new Window
        {
            Title = _searchTitle
        };
        window.SystemBackdrop = SystemBackdrop;
        var root = new Grid
        {
            Padding = new Thickness(18),
            MinWidth = 520,
            MinHeight = 180,
            RequestedTheme = string.Equals(_theme, "dark", StringComparison.OrdinalIgnoreCase)
                ? ElementTheme.Dark
                : ElementTheme.Light,
            Background = new SolidColorBrush(string.Equals(_theme, "dark", StringComparison.OrdinalIgnoreCase)
                ? Color.FromArgb(255, 31, 31, 31)
                : Color.FromArgb(255, 243, 243, 243))
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var prompt = new TextBlock
        {
            Text = _searchPrompt,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
            VerticalAlignment = VerticalAlignment.Center
        };
        root.Children.Add(prompt);
        var input = new TextBox
        {
            PlaceholderText = _searchTitle,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinHeight = 34,
            Margin = new Thickness(0, 0, 0, 12),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        input.Loaded += (_, _) => FocusSearchInput(input);
        Grid.SetRow(input, 1);
        root.Children.Add(input);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };
        var cancelButton = new Button { Content = _cancelButtonText, MinWidth = 96 };
        var advancedButton = new Button { Content = _advancedSearchButtonText, MinWidth = 96 };
        var searchButton = new Button { Content = _searchButtonText, MinWidth = 96 };
        buttons.Children.Add(advancedButton);
        buttons.Children.Add(cancelButton);
        buttons.Children.Add(searchButton);
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);
        window.Content = root;

        var completed = false;

        void Complete(SearchPromptResult? value)
        {
            if (completed)
            {
                return;
            }

            completed = true;
            result.TrySetResult(value);
            window.Close();
        }

        searchButton.Click += (_, _) => Complete(new SearchPromptResult(input.Text, false));
        advancedButton.Click += (_, _) => Complete(new SearchPromptResult(null, true));
        cancelButton.Click += (_, _) => Complete(null);
        input.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Enter)
            {
                Complete(new SearchPromptResult(input.Text, false));
                e.Handled = true;
            }
            else if (e.Key == VirtualKey.Escape)
            {
                Complete(null);
                e.Handled = true;
            }
        };
        window.Closed += (_, _) => result.TrySetResult(completed ? result.Task.Result : null);
        window.Activate();
        var hwnd = WindowNative.GetWindowHandle(window);
        var id = Win32Interop.GetWindowIdFromWindow(hwnd);
        if (AppWindow.GetFromWindowId(id) is { } appWindow)
        {
            appWindow.Resize(new SizeInt32(560, 220));
            var dark = string.Equals(_theme, "dark", StringComparison.OrdinalIgnoreCase);
            appWindow.TitleBar.BackgroundColor = dark ? Color.FromArgb(255, 31, 31, 31) : Color.FromArgb(255, 243, 243, 243);
            appWindow.TitleBar.ForegroundColor = dark ? Color.FromArgb(255, 243, 243, 243) : Color.FromArgb(255, 31, 31, 31);
            appWindow.TitleBar.ButtonBackgroundColor = appWindow.TitleBar.BackgroundColor;
            appWindow.TitleBar.ButtonForegroundColor = appWindow.TitleBar.ForegroundColor;
            if (appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsResizable = true;
                presenter.IsMaximizable = true;
                presenter.IsMinimizable = true;
            }
        }

        NativeMethods.SetForegroundWindow(hwnd);
        FocusSearchInput(input);
        _ = Task.Delay(120).ContinueWith(_ => DispatcherQueue.TryEnqueue(() => FocusSearchInput(input)));
        return await result.Task;
    }

    private static void FocusSearchInput(TextBox input)
    {
        input.Focus(FocusState.Programmatic);
        input.Select(input.Text.Length, 0);
    }

    private static IEnumerable<QuickMenuItem> FlattenSearchableItems(IEnumerable<QuickMenuItem> items)
    {
        foreach (var item in items)
        {
            if (item.IsSeparator || !item.IsEnabled)
            {
                continue;
            }

            if (item.IsFolder)
            {
                foreach (var child in FlattenSearchableItems(item.GetChildren()))
                {
                    yield return child;
                }

                continue;
            }

            yield return item;
        }
    }

    private static bool MatchesSearch(QuickMenuItem item, string query)
    {
        var filter = SearchFilter.Parse(query);
        if (filter.IsEmpty)
        {
            return true;
        }

        return filter.MatchesDate(item.CapturedAt ?? DateTimeOffset.Now)
            && filter.MatchesPinned(item.IsPinned)
            && filter.MatchesUrl(CliptonRuntime.ExtractUrls($"{item.Title} {item.Subtitle} {item.RevealedTitle}").Length > 0)
            && filter.MatchesType(item.Formats ?? [])
            && filter.MatchesText(() => $"{item.Title} {item.Subtitle} {item.KindLabel} {item.RevealedTitle}");
    }

    private void AddItems(IList<MenuFlyoutItemBase> target, IReadOnlyList<QuickMenuItem> items, MenuFlyoutSubItem? parent)
    {
        var focusableItems = parent is null
            ? _rootFocusableItems
            : _childFocusableItems.GetValueOrDefault(parent);
        if (focusableItems is null)
        {
            focusableItems = [];
            _childFocusableItems[parent!] = focusableItems;
        }

        foreach (var item in items)
        {
            if (item.IsSeparator)
            {
                target.Add(new MenuFlyoutSeparator());
                continue;
            }

            if (item.IsFolder)
            {
                var subItem = new MenuFlyoutSubItem
                {
                    Text = BuildDisplayText(item),
                    Icon = new FontIcon
                    {
                        Glyph = "\uE8B7",
                        FontFamily = new FontFamily("Segoe Fluent Icons")
                    }
                };
                focusableItems.Add(subItem);
                subItem.Tag = item;
                if (parent is not null)
                {
                    _parentItem[subItem] = parent;
                }

                _childFocusableItems[subItem] = [];
                AddFolderPlaceholder(subItem);
                subItem.PointerEntered += (_, _) => EnsureFolderItems(subItem);
                target.Add(subItem);
                continue;
            }

            if (parent is not null && item.PasteOptions is { Count: > 0 })
            {
                var optionSubItem = new MenuFlyoutSubItem
                {
                    Text = BuildDisplayText(item),
                    Icon = CreateIcon(item),
                    Tag = item
                };
                focusableItems.Add(optionSubItem);
                _parentItem[optionSubItem] = parent;
                _childFocusableItems[optionSubItem] = [];
                foreach (var option in item.PasteOptions)
                {
                    var optionItem = CreatePasteOptionMenuItem(option);
                    optionSubItem.Items.Add(optionItem);
                    _parentItem[optionItem] = optionSubItem;
                    _childFocusableItems[optionSubItem].Add(optionItem);
                }

                if (!string.Equals(_imagePreviewSize, "none", StringComparison.OrdinalIgnoreCase)
                    && item.IconImageBytes is { Length: > 0 })
                {
                    optionSubItem.Loaded += async (_, _) => await InsertImagePreviewAsync(optionSubItem, item.IconImageBytes, _imagePreviewSize);
                }

                target.Add(optionSubItem);
                continue;
            }

            var flyoutItem = new MenuFlyoutItem
            {
                Text = BuildDisplayText(item),
                KeyboardAcceleratorTextOverride = item.PasteOptions is { Count: > 0 }
                    ? BuildPasteOptionsHint(item)
                    : BuildCommandHint(item),
                Icon = CreateIcon(item)
            };
            if (item.PasteOptions is { Count: > 0 })
            {
                _childFocusableItems[flyoutItem] = [];
                flyoutItem.PointerEntered += (_, _) => _hoveredPasteOptionsItem = flyoutItem;
                flyoutItem.PointerExited += (_, _) =>
                {
                    if (ReferenceEquals(_hoveredPasteOptionsItem, flyoutItem))
                    {
                        _hoveredPasteOptionsItem = null;
                    }
                };
                flyoutItem.ContextRequested += (_, args) =>
                {
                    args.Handled = true;
                    ShowPasteOptionsForItem(flyoutItem, EnsurePasteOptionsFlyout(flyoutItem, item), focusOptions: false);
                };
                flyoutItem.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, args) =>
                {
                    if (!args.GetCurrentPoint(flyoutItem).Properties.IsRightButtonPressed)
                    {
                        return;
                    }

                    args.Handled = true;
                }), handledEventsToo: true);
                flyoutItem.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, args) =>
                {
                    if (!Equals(args.GetCurrentPoint(flyoutItem).Properties.PointerUpdateKind, Windows.UI.Input.PointerUpdateKind.RightButtonReleased))
                    {
                        return;
                    }

                    args.Handled = true;
                    DispatcherQueue.TryEnqueue(() => ShowPasteOptionsForItem(flyoutItem, EnsurePasteOptionsFlyout(flyoutItem, item), focusOptions: false));
                }), handledEventsToo: true);
                flyoutItem.RightTapped += (_, args) =>
                {
                    args.Handled = true;
                    ShowPasteOptionsForItem(flyoutItem, EnsurePasteOptionsFlyout(flyoutItem, item), focusOptions: false);
                };
            }

            if (!string.Equals(_imagePreviewSize, "none", StringComparison.OrdinalIgnoreCase)
                && item.IconImageBytes is { Length: > 0 })
            {
                flyoutItem.Loaded += async (_, _) => await InsertImagePreviewAsync(flyoutItem, item.IconImageBytes, _imagePreviewSize);
            }

            focusableItems.Add(flyoutItem);
            if (parent is not null)
            {
                _parentItem[flyoutItem] = parent;
            }

            flyoutItem.Tag = item;
            flyoutItem.Click += (_, _) => Invoke(item, asPlainText: false);
            target.Add(flyoutItem);
        }
    }

    private static void AddFolderPlaceholder(MenuFlyoutSubItem folderItem)
    {
        folderItem.Items.Add(new MenuFlyoutItem
        {
            Text = " ",
            IsEnabled = false,
            Visibility = Visibility.Collapsed
        });
    }

    private void EnsureFolderItems(MenuFlyoutSubItem folderItem)
    {
        if (!_materializedFolderItems.Add(folderItem)
            || folderItem.Tag is not QuickMenuItem item)
        {
            return;
        }

        folderItem.Items.Clear();
        _childFocusableItems[folderItem] = [];
        AddItems(folderItem.Items, item.GetChildren(), folderItem);
    }

    private MenuFlyout EnsurePasteOptionsFlyout(MenuFlyoutItemBase flyoutItem, QuickMenuItem item)
    {
        if (_pasteOptionsFlyouts.TryGetValue(flyoutItem, out var existing))
        {
            return existing;
        }

        var pasteOptionsFlyout = CreatePasteOptionsFlyout(item);
        flyoutItem.ContextFlyout = pasteOptionsFlyout;
        _pasteOptionsFlyouts[flyoutItem] = pasteOptionsFlyout;
        var optionItems = _childFocusableItems.GetValueOrDefault(flyoutItem);
        if (optionItems is null)
        {
            optionItems = [];
            _childFocusableItems[flyoutItem] = optionItems;
        }

        optionItems.Clear();
        foreach (var optionItem in pasteOptionsFlyout.Items.OfType<MenuFlyoutItemBase>())
        {
            _parentItem[optionItem] = flyoutItem;
            optionItems.Add(optionItem);
        }

        return pasteOptionsFlyout;
    }

    private MenuFlyout CreatePasteOptionsFlyout(QuickMenuItem item)
    {
        var flyout = new MenuFlyout
        {
            Placement = FlyoutPlacementMode.RightEdgeAlignedTop
        };

        foreach (var option in item.PasteOptions ?? [])
        {
            flyout.Items.Add(CreatePasteOptionMenuItem(option));
        }

        return flyout;
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

    private void ShowPasteOptionsForItem(MenuFlyoutItemBase flyoutItem, MenuFlyout pasteOptionsFlyout, bool focusOptions)
    {
        FocusMenuItemInCurrentContext(flyoutItem);
        pasteOptionsFlyout.ShowAt(flyoutItem);
        if (focusOptions
            && _childFocusableItems.TryGetValue(flyoutItem, out var optionItems)
            && optionItems.Count > 0)
        {
            _activeFocusableItems = optionItems;
            _activeParent = flyoutItem;
            FocusMenuItem(0);
        }
    }

    private void FocusMenuItemInCurrentContext(MenuFlyoutItemBase flyoutItem)
    {
        if (_parentItem.TryGetValue(flyoutItem, out var parent)
            && _childFocusableItems.TryGetValue(parent, out var childItems))
        {
            _activeFocusableItems = childItems;
            _activeParent = parent;
        }
        else
        {
            _activeFocusableItems = _rootFocusableItems;
            _activeParent = null;
        }

        FocusMenuItem(IndexOf(_activeFocusableItems, flyoutItem));
    }

    private string BuildDisplayText(QuickMenuItem item)
    {
        var title = _revealMaskedItems && !string.IsNullOrWhiteSpace(item.RevealedTitle)
            ? item.RevealedTitle!
            : item.Title;
        var text = FormatMenuText(title);
        return text;
    }

    private string BuildCommandHint(QuickMenuItem item)
    {
        var commandHint = _showShortcutHints ? item.CommandHint : string.Empty;
        if (!_showCapturedAt || item.CapturedAt is not { } capturedAt)
        {
            return commandHint;
        }

        var capturedAtText = capturedAt.LocalDateTime.ToString("yyyy/MM/dd HH:mm");
        return string.IsNullOrWhiteSpace(commandHint)
            ? capturedAtText
            : $"{commandHint}  |  {capturedAtText}";
    }

    private string BuildPasteOptionsHint(QuickMenuItem item)
    {
        var hint = BuildCommandHint(item);
        return string.IsNullOrWhiteSpace(hint) ? "..." : $"{hint}  ...";
    }

    private QuickMenuItem? GetFocusedMenuItem()
    {
        if (GetTrackedFocusedMenuItemBase() is { Tag: QuickMenuPasteOption })
        {
            return null;
        }

        if (GetTrackedFocusedMenuItemBase() is { Tag: QuickMenuItem trackedItem })
        {
            return trackedItem;
        }

        if (_host.XamlRoot is null)
        {
            return _navigator?.SelectedItem;
        }

        return FocusManager.GetFocusedElement(_host.XamlRoot) switch
        {
            MenuFlyoutItem { Tag: QuickMenuItem item } => item,
            MenuFlyoutSubItem { Tag: QuickMenuItem item } => item,
            _ => GetTrackedFocusedMenuItemBase() is { Tag: QuickMenuItem item } ? item : _navigator?.SelectedItem
        };
    }

    private QuickMenuPasteOption? GetFocusedPasteOption()
    {
        if (GetTrackedFocusedMenuItemBase() is { Tag: QuickMenuPasteOption trackedOption })
        {
            return trackedOption;
        }

        if (_host.XamlRoot is not null
            && FocusManager.GetFocusedElement(_host.XamlRoot) is MenuFlyoutItem { Tag: QuickMenuPasteOption focusedOption })
        {
            return focusedOption;
        }

        return null;
    }

    private MenuFlyoutItemBase? GetFocusedMenuItemBase()
    {
        if (GetTrackedFocusedMenuItemBase() is { } trackedItem)
        {
            return trackedItem;
        }

        if (_host.XamlRoot is not null
            && FocusManager.GetFocusedElement(_host.XamlRoot) is MenuFlyoutItemBase focusedItem)
        {
            return focusedItem;
        }

        return GetTrackedFocusedMenuItemBase();
    }

    private MenuFlyoutItemBase? GetTrackedFocusedMenuItemBase()
    {
        return _focusedIndex >= 0 && _focusedIndex < _activeFocusableItems.Count
            ? _activeFocusableItems[_focusedIndex]
            : null;
    }

    private void FocusMenuItem(int index)
    {
        if (_activeFocusableItems.Count == 0)
        {
            return;
        }

        _focusedIndex = (index + _activeFocusableItems.Count) % _activeFocusableItems.Count;
        _activeFocusableItems[_focusedIndex].Focus(FocusState.Keyboard);
        SyncNavigatorSelectionFromTrackedFocus();
    }

    private void MoveFocus(int delta)
    {
        SyncFocusedIndex();
        FocusMenuItem(_focusedIndex + delta);
    }

    private void SyncFocusedIndex()
    {
        if (_host.XamlRoot is null)
        {
            return;
        }

        var focusedElement = FocusManager.GetFocusedElement(_host.XamlRoot);
        if (focusedElement is MenuFlyoutItemBase focusedItem
            && _parentItem.TryGetValue(focusedItem, out var parent)
            && _childFocusableItems.TryGetValue(parent, out var childItems))
        {
            _activeFocusableItems = childItems;
            _activeParent = parent;
        }
        else if (focusedElement is MenuFlyoutItemBase rootItem && _rootFocusableItems.Contains(rootItem))
        {
            _activeFocusableItems = _rootFocusableItems;
            _activeParent = null;
        }

        for (var i = 0; i < _activeFocusableItems.Count; i++)
        {
            if (ReferenceEquals(_activeFocusableItems[i], focusedElement))
            {
                _focusedIndex = i;
                return;
            }
        }
    }

    private void EnterChildFocusContext()
    {
        if (_host.XamlRoot is null)
        {
            return;
        }

        if (_activeParent is null)
        {
            SyncFocusedIndex();
        }

        if (GetFocusedMenuItemBase() is not { } focusedItem
            || focusedItem.Tag is not QuickMenuItem focusedQuickMenuItem
            || !_childFocusableItems.TryGetValue(focusedItem, out var childItems))
        {
            SyncFocusedIndex();
            return;
        }

        if (focusedItem is MenuFlyoutSubItem folderItem && focusedQuickMenuItem.IsFolder)
        {
            PrepareFolderSubmenuForNativeOpen(folderItem);
            return;
        }

        if (focusedQuickMenuItem.PasteOptions is { Count: > 0 })
        {
            var pasteOptionsFlyout = EnsurePasteOptionsFlyout(focusedItem, focusedQuickMenuItem);
            ShowPasteOptionsForItem(focusedItem, pasteOptionsFlyout, focusOptions: true);
            return;
        }

        if (childItems.Count == 0)
        {
            SyncFocusedIndex();
            return;
        }

        _activeFocusableItems = childItems;
        _activeParent = focusedItem;
        FocusMenuItem(0);
    }

    private void ReturnToParentFocusContext()
    {
        if (_activeParent is null)
        {
            SyncFocusedIndex();
            return;
        }

        var parent = _activeParent;
        if (_pasteOptionsFlyouts.TryGetValue(parent, out var pasteOptionsFlyout))
        {
            pasteOptionsFlyout.Hide();
        }

        if (_parentItem.TryGetValue(parent, out var grandParent))
        {
            _activeFocusableItems = _childFocusableItems[grandParent];
            _activeParent = grandParent;
        }
        else
        {
            _activeFocusableItems = _rootFocusableItems;
            _activeParent = null;
        }

        FocusMenuItem(IndexOf(_activeFocusableItems, parent));
    }

    private void PrepareFolderSubmenuForNativeOpen(MenuFlyoutSubItem folderItem)
    {
        if (folderItem.Tag is not QuickMenuItem { IsFolder: true })
        {
            SyncFocusedIndex();
            return;
        }

        EnsureFolderItems(folderItem);
        if (!_childFocusableItems.TryGetValue(folderItem, out var childItems)
            || childItems.Count == 0)
        {
            SyncFocusedIndex();
            return;
        }

        _activeFocusableItems = childItems;
        _activeParent = folderItem;
        _focusedIndex = 0;
    }

    private void HandleLeftNavigation()
    {
        ReturnToParentFocusContext();
    }

    private MenuFlyoutItemBase? GetLeftNavigationTarget()
    {
        if (GetTrackedFocusedMenuItemBase() is { } trackedItem)
        {
            return trackedItem;
        }

        return GetFocusedMenuItemBase();
    }

    private void SyncNavigatorSelectionFromTrackedFocus()
    {
        if (_activeParent is not null
            || GetTrackedFocusedMenuItemBase() is not { Tag: QuickMenuItem item })
        {
            return;
        }

        var index = IndexOf(_currentItems, item);
        if (index >= 0)
        {
            _navigator?.Select(index);
        }
    }

    private static int IndexOf(IReadOnlyList<MenuFlyoutItemBase> items, MenuFlyoutItemBase item)
    {
        for (var i = 0; i < items.Count; i++)
        {
            if (ReferenceEquals(items[i], item))
            {
                return i;
            }
        }

        return 0;
    }

    private static int IndexOf(IReadOnlyList<QuickMenuItem> items, QuickMenuItem item)
    {
        for (var i = 0; i < items.Count; i++)
        {
            if (ReferenceEquals(items[i], item))
            {
                return i;
            }
        }

        return -1;
    }

    private void Invoke(QuickMenuItem item, bool asPlainText)
    {
        var action = asPlainText ? item.PlainTextInvoke : item.Invoke;
        if (action is null)
        {
            return;
        }

        _flyout.Hide();
        _appWindow?.Hide();
        _ = Task.Delay(90).ContinueWith(_ => DispatcherQueue.TryEnqueue(() =>
        {
            action();
            Dismiss();
        }));
    }

    private void InvokePasteOption(QuickMenuPasteOption option)
    {
        _flyout.Hide();
        _appWindow?.Hide();
        _ = Task.Delay(90).ContinueWith(_ => DispatcherQueue.TryEnqueue(() =>
        {
            option.Invoke();
            Dismiss();
        }));
    }

    private void PositionNearCursor()
    {
        _hwnd = WindowNative.GetWindowHandle(this);
        var id = Win32Interop.GetWindowIdFromWindow(_hwnd);
        _appWindow = AppWindow.GetFromWindowId(id);
        MakeHostWindowTransparent();
        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }

        var point = GetCursorPoint();
        var workingArea = GetWorkingArea(point);
        var placement = GetRootFlyoutPlacement(point, workingArea);
        _flyout.Placement = placement.Placement;
        _appWindow.Resize(new SizeInt32(HostWindowSize, HostWindowSize));
        _appWindow.Move(new PointInt32(placement.Anchor.X, placement.Anchor.Y));
        NativeMethods.SetForegroundWindow(_hwnd);
    }

    private static RootFlyoutPlacement GetRootFlyoutPlacement(System.Drawing.Point point, System.Drawing.Rectangle workingArea)
    {
        var minX = workingArea.Left + ScreenEdgePadding;
        var minY = workingArea.Top + ScreenEdgePadding;
        var maxX = Math.Max(minX, workingArea.Right - ScreenEdgePadding);
        var maxY = Math.Max(minY, workingArea.Bottom - ScreenEdgePadding);
        var anchor = new System.Drawing.Point(
            Math.Clamp(point.X, minX, maxX),
            Math.Clamp(point.Y, minY, maxY));
        var openAbove = anchor.Y + EstimatedRootFlyoutHeight > workingArea.Bottom - ScreenEdgePadding
            && anchor.Y - workingArea.Top > workingArea.Bottom - anchor.Y;
        var placement = openAbove ? FlyoutPlacementMode.Top : FlyoutPlacementMode.Bottom;

        return new RootFlyoutPlacement(anchor, placement);
    }

    private static System.Drawing.Point GetCursorPoint()
    {
        return NativeMethods.GetCursorPos(out var point)
            ? new System.Drawing.Point(point.X, point.Y)
            : System.Drawing.Point.Empty;
    }

    private static System.Drawing.Rectangle GetWorkingArea(System.Drawing.Point point)
    {
        var nativePoint = new NativeMethods.Point { X = point.X, Y = point.Y };
        var monitor = NativeMethods.MonitorFromPoint(nativePoint, NativeMethods.MonitorDefaultToNearest);
        var info = new NativeMethods.MonitorInfo
        {
            Size = (uint)Marshal.SizeOf<NativeMethods.MonitorInfo>()
        };
        if (monitor == IntPtr.Zero || !NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            return new System.Drawing.Rectangle(point.X, point.Y, HostWindowSize, HostWindowSize);
        }

        return System.Drawing.Rectangle.FromLTRB(info.Work.Left, info.Work.Top, info.Work.Right, info.Work.Bottom);
    }

    private static string FormatMenuText(string text)
    {
        var normalized = string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length <= MaxMenuLineLength)
        {
            return normalized;
        }

        var line1 = normalized[..MaxMenuLineLength].TrimEnd();
        var remaining = normalized[MaxMenuLineLength..].TrimStart();
        var line2 = remaining.Length <= MaxMenuLineLength
            ? remaining
            : $"{remaining[..(MaxMenuLineLength - 1)].TrimEnd()}\u2026";

        return $"{line1}{Environment.NewLine}{line2}";
    }

    private static IconElement? CreateIcon(QuickMenuItem item)
    {
        if (string.IsNullOrWhiteSpace(item.IconGlyph))
        {
            return null;
        }

        return new FontIcon
        {
            Glyph = item.IconGlyph,
            FontFamily = new FontFamily(item.IconFontFamily ?? "Segoe UI"),
            FontSize = item.IconImageBytes is { Length: > 0 } ? 13 : 12,
            Opacity = item.IconImageBytes is { Length: > 0 } ? 0.78 : 1
        };
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
            FontSize = 12
        };
    }

    private static async Task InsertImagePreviewAsync(MenuFlyoutItemBase flyoutItem, byte[]? imageBytes, string size)
    {
        if (imageBytes is not { Length: > 0 })
        {
            return;
        }

        var textBlock = FindDescendant<TextBlock>(flyoutItem, "TextBlock");
        if (textBlock?.Parent is not Grid grid || textBlock.Parent is StackPanel)
        {
            return;
        }

        var originalMargin = textBlock.Margin;
        var column = Grid.GetColumn(textBlock);
        grid.Children.Remove(textBlock);
        textBlock.Margin = new Thickness(0);
        textBlock.VerticalAlignment = VerticalAlignment.Center;

        var dimensions = GetImagePreviewDimensions(size);
        var preview = new Border
        {
            Width = dimensions.Width,
            Height = dimensions.Height,
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
            Background = new SolidColorBrush(Color.FromArgb(32, 255, 255, 255)),
            Child = new Image
            {
                Source = await CreateBitmapImageAsync(imageBytes),
                Stretch = Stretch.UniformToFill
            }
        };

        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 11,
            Margin = originalMargin,
            VerticalAlignment = VerticalAlignment.Center
        };
        panel.Children.Add(preview);
        panel.Children.Add(textBlock);

        Grid.SetColumn(panel, column);
        grid.Children.Add(panel);
    }

    private static async Task<BitmapImage> CreateBitmapImageAsync(byte[] bytes)
    {
        var bitmap = new BitmapImage();
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream))
        {
            writer.WriteBytes(bytes);
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
        }

        stream.Seek(0);
        await bitmap.SetSourceAsync(stream);
        return bitmap;
    }

    private static (double Width, double Height) GetImagePreviewDimensions(string size)
    {
        return NormalizeImagePreviewSize(size) switch
        {
            "small" => (40, 28),
            "large" => (88, 60),
            _ => (64, 44)
        };
    }

    private static string NormalizeImagePreviewSize(string? size)
    {
        return size?.ToLowerInvariant() switch
        {
            "none" or "small" or "large" => size.ToLowerInvariant(),
            _ => "medium"
        };
    }

    private static T? FindDescendant<T>(DependencyObject root, string? name = null)
        where T : FrameworkElement
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T element && (name is null || element.Name == name))
            {
                return element;
            }

            var descendant = FindDescendant<T>(child, name);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }

    private void MakeHostWindowTransparent()
    {
        var exStyle = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GwlExstyle).ToInt64();
        exStyle |= NativeMethods.WsExLayered | NativeMethods.WsExToolwindow;
        exStyle &= ~NativeMethods.WsExAppwindow;
        NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GwlExstyle, new IntPtr(exStyle));
        NativeMethods.SetLayeredWindowAttributes(_hwnd, 0, 1, NativeMethods.LwaAlpha);
    }
}

public sealed record QuickMenuItem(
    string Title,
    string Subtitle,
    string KindLabel,
    string CommandHint,
    Action Invoke,
    Action? PlainTextInvoke = null,
    bool IsEnabled = true,
    IReadOnlyList<QuickMenuItem>? Children = null,
    Func<IReadOnlyList<QuickMenuItem>>? LazyChildren = null,
    bool IsSeparator = false,
    IReadOnlyList<QuickMenuPasteOption>? PasteOptions = null,
    string? IconGlyph = null,
    string? IconFontFamily = null,
    byte[]? IconImageBytes = null,
    string? RevealedTitle = null,
    DateTimeOffset? CapturedAt = null,
    bool IsPinned = false,
    IReadOnlyCollection<ClipboardFormatKind>? Formats = null)
{
    private IReadOnlyList<QuickMenuItem>? _resolvedChildren = Children;

    public bool IsSelected { get; set; }

    public bool IsFolder => !IsSeparator && (_resolvedChildren is { Count: > 0 } || LazyChildren is not null);

    public Visibility FolderVisibility => IsFolder ? Visibility.Visible : Visibility.Collapsed;

    public string DisplayHint => IsFolder ? "\uE974" : CommandHint;

    public static QuickMenuItem Separator() => new(string.Empty, string.Empty, string.Empty, string.Empty, () => { }, IsEnabled: false, IsSeparator: true);

    public IReadOnlyList<QuickMenuItem> GetChildren()
    {
        if (_resolvedChildren is not null)
        {
            return _resolvedChildren;
        }

        _resolvedChildren = LazyChildren?.Invoke() ?? [];
        return _resolvedChildren;
    }

    public override string ToString() => Title;
}

public sealed record QuickMenuPasteOption(
    string Text,
    string IconGlyph,
    Action Invoke,
    string? IconFontFamily = null);

internal sealed record RootFlyoutPlacement(
    System.Drawing.Point Anchor,
    FlyoutPlacementMode Placement);

internal sealed record SearchPromptResult(string? Query, bool OpenDetailedSearch);
