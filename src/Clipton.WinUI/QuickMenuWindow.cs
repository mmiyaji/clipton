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
using Windows.System;
using Windows.UI;
using WinRT.Interop;
using Forms = System.Windows.Forms;

namespace Clipton.WinUI;

public sealed class QuickMenuWindow : Window
{
    private const int MaxMenuLineLength = 34;
    private const int HostWindowSize = 1;
    private const int ScreenEdgePadding = 8;
    private const int EstimatedRootFlyoutWidth = 380;
    private const int EstimatedRootFlyoutHeight = 420;
    private readonly QuickMenuNavigator _navigator;
    private readonly IReadOnlyList<QuickMenuItem> _rootItems;
    private readonly Grid _host = new();
    private readonly MenuFlyout _flyout = new();
    private readonly string _theme;
    private readonly string _searchTitle;
    private readonly string _searchPrompt;
    private readonly string _searchButtonText;
    private readonly string _cancelButtonText;
    private readonly string _noSearchResultsText;
    private readonly string _imagePreviewSize;
    private readonly NativeMethods.LowLevelKeyboardProc _keyboardProc;
    private readonly NativeMethods.LowLevelMouseProc _mouseProc;
    private readonly List<MenuFlyoutItemBase> _rootFocusableItems = [];
    private readonly Dictionary<MenuFlyoutSubItem, List<MenuFlyoutItemBase>> _childFocusableItems = [];
    private readonly Dictionary<MenuFlyoutItemBase, MenuFlyoutSubItem> _parentItem = [];
    private IReadOnlyList<MenuFlyoutItemBase> _activeFocusableItems = [];
    private MenuFlyoutSubItem? _activeParent;
    private int _focusedIndex = -1;
    private AppWindow? _appWindow;
    private IntPtr _hwnd;
    private IntPtr _keyboardHook;
    private IntPtr _mouseHook;
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
        string searchTitle,
        string searchPrompt,
        string searchButtonText,
        string cancelButtonText,
        string noSearchResultsText)
    {
        _navigator = new QuickMenuNavigator(title, items);
        _rootItems = items;
        _currentItems = items;
        _theme = theme;
        _imagePreviewSize = NormalizeImagePreviewSize(imagePreviewSize);
        _searchTitle = searchTitle;
        _searchPrompt = searchPrompt;
        _searchButtonText = searchButtonText;
        _cancelButtonText = cancelButtonText;
        _noSearchResultsText = noSearchResultsText;
        _keyboardProc = OnKeyboardHook;
        _mouseProc = OnMouseHook;
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
        });
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
        if (_keyboardHook == IntPtr.Zero)
        {
            _keyboardHook = NativeMethods.SetWindowsHookEx(
                NativeMethods.WhKeyboardLl,
                _keyboardProc,
                NativeMethods.GetModuleHandle(null),
                0);
        }
    }

    private void UninstallKeyboardHook()
    {
        if (_keyboardHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
        }
    }

    private void InstallMouseHook()
    {
        if (_mouseHook == IntPtr.Zero)
        {
            _mouseHook = NativeMethods.SetWindowsHookEx(
                NativeMethods.WhMouseLl,
                _mouseProc,
                NativeMethods.GetModuleHandle(null),
                0);
        }
    }

    private void UninstallMouseHook()
    {
        if (_mouseHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }
    }

    private IntPtr OnMouseHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0 || _host.XamlRoot is null)
        {
            return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        }

        var message = wParam.ToInt32();
        if (message is not NativeMethods.WmRbuttondown and not NativeMethods.WmRbuttonup)
        {
            return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        }

        var focusedElement = FocusManager.GetFocusedElement(_host.XamlRoot);
        var targetItem = _hoveredPasteOptionsItem ?? focusedElement as MenuFlyoutItemBase;
        if (targetItem is not { } flyoutItem
            || flyoutItem.ContextFlyout is not MenuFlyout pasteOptionsFlyout)
        {
            return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        }

        if (message == NativeMethods.WmRbuttonup)
        {
            DispatcherQueue.TryEnqueue(() => ShowPasteOptionsForItem(flyoutItem, pasteOptionsFlyout));
        }

        return 1;
    }

    private IntPtr OnKeyboardHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0 || (wParam.ToInt32() != NativeMethods.WmKeydown && wParam.ToInt32() != NativeMethods.WmSyskeydown))
        {
            return NativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
        }

        var handled = true;
        var key = Marshal.ReadInt32(lParam);
        var controlDown = (NativeMethods.GetAsyncKeyState(NativeMethods.VkControl) & 0x8000) != 0;
        if (controlDown && key == NativeMethods.VkS)
        {
            DispatcherQueue.TryEnqueue(SearchMenu);
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
                if (_activeParent is not null)
                {
                    DispatcherQueue.TryEnqueue(ReturnToParentFocusContext);
                    handled = false;
                }
                break;
            case NativeMethods.VkRight:
                if (GetFocusedMenuItemBase() is MenuFlyoutSubItem)
                {
                    DispatcherQueue.TryEnqueue(EnterChildFocusContext);
                    handled = false;
                }
                break;
            case NativeMethods.VkReturn:
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
            case NativeMethods.VkM:
                DispatcherQueue.TryEnqueue(() => ToggleDisplayMode(revealMaskedItems: !_revealMaskedItems));
                break;
            case NativeMethods.VkD when controlDown:
                DispatcherQueue.TryEnqueue(() => ToggleDisplayMode(showCapturedAt: !_showCapturedAt));
                break;
            case 'T':
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (GetFocusedMenuItem() is { } item)
                    {
                        Invoke(item, asPlainText: true);
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
            : NativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
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
        var query = await PromptForSearchAsync();
        if (query is null)
        {
            _opened = false;
            BuildFlyout();
            ShowFlyout();
            EnqueueAfterDelay(300, () => _rebuildingFlyout = false);
            return;
        }

        var normalizedQuery = query.Trim();
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
                    "Ctrl+S",
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
            _searchTitle,
            _searchPrompt,
            _searchButtonText,
            _cancelButtonText,
            _noSearchResultsText);
        _replacementWindow.Dismissed += (_, _) => Dismiss();
        _replacementWindow.FocusMenu();
    }

    private async Task<string?> PromptForSearchAsync()
    {
        var result = new TaskCompletionSource<string?>();
        var window = new Window
        {
            Title = _searchTitle
        };
        window.SystemBackdrop = SystemBackdrop;
        var root = new Grid
        {
            Padding = new Thickness(18),
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
            Margin = new Thickness(0, 0, 0, 12)
        };
        root.Children.Add(prompt);
        var input = new TextBox
        {
            PlaceholderText = _searchTitle,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 0, 0, 16)
        };
        Grid.SetRow(input, 1);
        root.Children.Add(input);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };
        var cancelButton = new Button { Content = _cancelButtonText, MinWidth = 96 };
        var searchButton = new Button { Content = _searchButtonText, MinWidth = 96 };
        buttons.Children.Add(cancelButton);
        buttons.Children.Add(searchButton);
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);
        window.Content = root;

        var completed = false;
        void Complete(string? value)
        {
            if (completed)
            {
                return;
            }

            completed = true;
            result.TrySetResult(value);
            window.Close();
        }

        searchButton.Click += (_, _) => Complete(input.Text);
        cancelButton.Click += (_, _) => Complete(null);
        input.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Enter)
            {
                Complete(input.Text);
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
            appWindow.Resize(new SizeInt32(640, 188));
            if (appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsResizable = false;
                presenter.IsMaximizable = false;
                presenter.IsMinimizable = false;
            }
        }

        NativeMethods.SetForegroundWindow(hwnd);
        _ = DispatcherQueue.TryEnqueue(() => input.Focus(FocusState.Programmatic));
        return await result.Task;
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
                AddItems(subItem.Items, item.GetChildren(), subItem);
                target.Add(subItem);
                continue;
            }

            if (item.PasteOptions is { Count: > 0 })
            {
                var optionSubItem = new MenuFlyoutSubItem
                {
                    Text = BuildDisplayText(item),
                    Icon = CreateIcon(item),
                    Tag = item
                };
                focusableItems.Add(optionSubItem);
                if (parent is not null)
                {
                    _parentItem[optionSubItem] = parent;
                }

                _childFocusableItems[optionSubItem] = [];
                foreach (var option in item.PasteOptions)
                {
                    var optionItem = CreatePasteOptionMenuItem(option);
                    optionSubItem.Items.Add(optionItem);
                    _parentItem[optionItem] = optionSubItem;
                    _childFocusableItems[optionSubItem].Add(optionItem);
                }

                if (!string.Equals(_imagePreviewSize, "none", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(item.IconImagePath)
                    && File.Exists(item.IconImagePath))
                {
                    optionSubItem.Loaded += (_, _) => InsertImagePreview(optionSubItem, item.IconImagePath, _imagePreviewSize);
                }

                target.Add(optionSubItem);
                continue;
            }

            var flyoutItem = new MenuFlyoutItem
            {
                Text = BuildDisplayText(item),
                KeyboardAcceleratorTextOverride = BuildCommandHint(item),
                Icon = CreateIcon(item)
            };
            if (item.PasteOptions is { Count: > 0 })
            {
                var pasteOptionsFlyout = CreatePasteOptionsFlyout(item);
                flyoutItem.ContextFlyout = pasteOptionsFlyout;
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
                    ShowPasteOptionsForItem(flyoutItem, pasteOptionsFlyout);
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
                    DispatcherQueue.TryEnqueue(() => ShowPasteOptionsForItem(flyoutItem, pasteOptionsFlyout));
                }), handledEventsToo: true);
                flyoutItem.RightTapped += (_, args) =>
                {
                    args.Handled = true;
                    ShowPasteOptionsForItem(flyoutItem, pasteOptionsFlyout);
                };
            }

            if (!string.Equals(_imagePreviewSize, "none", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(item.IconImagePath)
                && File.Exists(item.IconImagePath))
            {
                flyoutItem.Loaded += (_, _) => InsertImagePreview(flyoutItem, item.IconImagePath, _imagePreviewSize);
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

    private void ShowPasteOptionsForItem(MenuFlyoutItemBase flyoutItem, MenuFlyout pasteOptionsFlyout)
    {
        FocusMenuItemInCurrentContext(flyoutItem);
        pasteOptionsFlyout.ShowAt(flyoutItem);
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
        if (!_showCapturedAt || item.CapturedAt is not { } capturedAt)
        {
            return item.CommandHint;
        }

        var capturedAtText = capturedAt.LocalDateTime.ToString("yyyy/MM/dd HH:mm");
        return string.IsNullOrWhiteSpace(item.CommandHint)
            ? capturedAtText
            : $"{item.CommandHint}  |  {capturedAtText}";
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
            return _navigator.SelectedItem;
        }

        return FocusManager.GetFocusedElement(_host.XamlRoot) switch
        {
            MenuFlyoutItem { Tag: QuickMenuItem item } => item,
            MenuFlyoutSubItem { Tag: QuickMenuItem item } => item,
            _ => GetTrackedFocusedMenuItemBase() is { Tag: QuickMenuItem item } ? item : _navigator.SelectedItem
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

        if (GetFocusedMenuItemBase() is not MenuFlyoutSubItem focusedFolder
            || !_childFocusableItems.TryGetValue(focusedFolder, out var childItems)
            || childItems.Count == 0)
        {
            SyncFocusedIndex();
            return;
        }

        _activeFocusableItems = childItems;
        _activeParent = focusedFolder;
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
            _navigator.Select(index);
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

        var point = Forms.Cursor.Position;
        var workingArea = Forms.Screen.FromPoint(point).WorkingArea;
        var anchor = GetRootFlyoutAnchor(point, workingArea);
        _flyout.Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft;
        _appWindow.Resize(new SizeInt32(HostWindowSize, HostWindowSize));
        _appWindow.Move(new PointInt32(anchor.X, anchor.Y));
        NativeMethods.SetForegroundWindow(_hwnd);
    }

    private static System.Drawing.Point GetRootFlyoutAnchor(System.Drawing.Point point, System.Drawing.Rectangle workingArea)
    {
        var minX = workingArea.Left + ScreenEdgePadding;
        var minY = workingArea.Top + ScreenEdgePadding;
        var maxX = Math.Max(minX, workingArea.Right - ScreenEdgePadding - EstimatedRootFlyoutWidth);
        var maxY = Math.Max(minY, workingArea.Bottom - ScreenEdgePadding - EstimatedRootFlyoutHeight);
        return new System.Drawing.Point(
            Math.Clamp(point.X, minX, maxX),
            Math.Clamp(point.Y, minY, maxY));
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
            FontSize = !string.IsNullOrWhiteSpace(item.IconImagePath) ? 13 : 12,
            Opacity = !string.IsNullOrWhiteSpace(item.IconImagePath) ? 0.78 : 1
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

    private static void InsertImagePreview(MenuFlyoutItemBase flyoutItem, string? imagePath, string size)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
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
                Source = new BitmapImage(new Uri(imagePath)),
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
    string? IconImagePath = null,
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
