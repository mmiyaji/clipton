using System.Runtime.InteropServices;
using Clipton.Core;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI;
using WinRT.Interop;

namespace Clipton.WinUI;

public sealed class QuickMenuWindow : Window, IQuickMenuHostWindow
{
    private const int MaxMenuLineLength = 34;
    private const int HostWindowSize = 1;
    private const int ScreenEdgePadding = 8;
    private const int EstimatedRootFlyoutHeight = 420;
    private const int NativeResourceCollectionMinIntervalMilliseconds = 5000;
    private const int DwmwaNcRenderingPolicy = 2;
    private const int DwmwaBorderColor = 34;
    private const int DwmncrpDisabled = 1;
    private const int DwmwaColorNone = unchecked((int)0xFFFFFFFE);
    private const int GwlStyle = -16;
    private const long WsCaption = 0x00C00000L;
    private const long WsThickFrame = 0x00040000L;
    private const long WsBorder = 0x00800000L;
    private const long WsDlgFrame = 0x00400000L;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private const double ImagePreviewBaseImageWidth = 520;
    private const double ImagePreviewBaseImageHeight = 300;
    private const double ImagePreviewBaseWindowWidth = 560;
    private const double ImagePreviewBaseWindowHeight = 420;
    private const double ImagePreviewMinZoom = 0.5;
    private const double ImagePreviewMaxZoom = 3.0;
    private const double ImagePreviewZoomStep = 0.15;
    private const string AppName = "Clipton";
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
    private readonly string _previewImageText;
    private readonly IReadOnlyDictionary<string, string> _previewStrings;
    private readonly string _imagePreviewSize;
    private readonly Action _openDetailedSearch;
    private readonly QuickMenuShortcutSettings _shortcuts;
    private readonly bool _showShortcutHints;
    private readonly List<MenuFlyoutItemBase> _rootFocusableItems = [];
    private readonly Dictionary<MenuFlyoutItemBase, List<MenuFlyoutItemBase>> _childFocusableItems = [];
    private readonly Dictionary<MenuFlyoutItemBase, MenuFlyoutItemBase> _parentItem = [];
    private readonly Dictionary<MenuFlyoutItemBase, MenuFlyout> _pasteOptionsFlyouts = [];
    private readonly HashSet<MenuFlyoutSubItem> _materializedFolderItems = [];
    private readonly HashSet<MenuFlyoutSubItem> _materializingFolderItems = [];
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
    private Window? _imagePreviewWindow;
    private IntPtr _imagePreviewHwnd;
    private AppWindow? _imagePreviewAppWindow;
    private bool _draggingImagePreview;
    private bool _imagePreviewDragged;
    private System.Drawing.Point _imagePreviewDragCursorOrigin;
    private PointInt32 _imagePreviewDragWindowOrigin;
    private bool _openingImagePreview;
    private QuickMenuItem? _imagePreviewItem;
    private Image? _imagePreviewImage;
    private Border? _imagePreviewFrame;
    private Border? _imagePreviewFeedbackPill;
    private TextBlock? _imagePreviewFeedbackText;
    private double _imagePreviewZoom = 1.0;
    private long _imagePreviewRequestId;
    private long _imagePreviewFeedbackRequestId;

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
        string noSearchResultsText,
        string previewImageText,
        IReadOnlyDictionary<string, string> previewStrings)
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
        _previewImageText = previewImageText;
        _previewStrings = previewStrings;
        Title = "Clipton";
        BuildHost();
        BuildFlyout();
        PositionNearCursor();
        _flyout.Closed += (_, _) =>
        {
            if (_openingImagePreview)
            {
                return;
            }

            if (!_rebuildingFlyout)
            {
                Dismiss();
            }
        };
    }

    public event EventHandler? Dismissed;

    public string DisplayMode => "default";

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

        HideImagePreview();
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

        if (s_keyboardHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(s_keyboardHook);
            s_keyboardHook = IntPtr.Zero;
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

        if (s_mouseHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(s_mouseHook);
            s_mouseHook = IntPtr.Zero;
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
        var modifiers = GetCurrentModifierState();
        if (!IsInteractionForeground())
        {
            return NativeMethods.CallNextHookEx(s_keyboardHook, nCode, wParam, lParam);
        }

        if (_imagePreviewWindow is not null && HandleImagePreviewShortcut(key, modifiers))
        {
            return 1;
        }

        if (MatchesShortcut(key, modifiers, _shortcuts.Search))
        {
            DispatcherQueue.TryEnqueue(SearchMenu);
            return 1;
        }

        if (MatchesShortcut(key, modifiers, _shortcuts.ToggleMaskReveal))
        {
            DispatcherQueue.TryEnqueue(() => ToggleDisplayMode(revealMaskedItems: !_revealMaskedItems));
            return 1;
        }

        if (MatchesShortcut(key, modifiers, _shortcuts.ToggleCapturedAt))
        {
            DispatcherQueue.TryEnqueue(() => ToggleDisplayMode(showCapturedAt: !_showCapturedAt));
            return 1;
        }

        if (MatchesShortcut(key, modifiers, _shortcuts.PastePlainText))
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
                if (_imagePreviewWindow is not null)
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        if (GetFocusedMenuItem() is { } item)
                        {
                            Invoke(item, asPlainText: false);
                        }
                    });
                    break;
                }

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
                    else if (GetFocusedImagePreviewCommand() is { } previewCommand)
                    {
                        ToggleImagePreview(previewCommand.Item);
                    }
                    else if (GetFocusedMenuItem() is { } item)
                    {
                        Invoke(item, asPlainText: false);
                    }
                });
                break;
            case NativeMethods.VkSpace:
                if (GetFocusedMenuItem() is { } previewItem && HasImagePreview(previewItem))
                {
                    DispatcherQueue.TryEnqueue(() => ToggleImagePreview(previewItem));
                }
                else
                {
                    handled = false;
                }
                break;
            case NativeMethods.VkEscape:
                if (_imagePreviewWindow is not null)
                {
                    DispatcherQueue.TryEnqueue(HideImagePreview);
                }
                else
                {
                    Dismiss();
                }
                break;
            default:
                handled = false;
                break;
        }

        return handled
            ? 1
            : NativeMethods.CallNextHookEx(s_keyboardHook, nCode, wParam, lParam);
    }

    private static KeyModifierState GetCurrentModifierState()
    {
        static bool IsDown(int key) => (NativeMethods.GetAsyncKeyState(key) & 0x8000) != 0;

        return new KeyModifierState(
            IsDown(NativeMethods.VkControl),
            IsDown(NativeMethods.VkShift),
            IsDown(NativeMethods.VkMenu),
            IsDown(NativeMethods.VkLWin) || IsDown(NativeMethods.VkRWin));
    }

    private static bool MatchesShortcut(int key, KeyModifierState modifiers, string shortcut)
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

        var shortcutModifiers = KeyModifierState.None;
        foreach (var part in parts.Take(parts.Length - 1))
        {
            if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)
                || part.Equals("Control", StringComparison.OrdinalIgnoreCase))
            {
                shortcutModifiers = shortcutModifiers with { Control = true };
            }
            else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase))
            {
                shortcutModifiers = shortcutModifiers with { Shift = true };
            }
            else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase))
            {
                shortcutModifiers = shortcutModifiers with { Alt = true };
            }
            else if (part.Equals("Win", StringComparison.OrdinalIgnoreCase)
                || part.Equals("Windows", StringComparison.OrdinalIgnoreCase))
            {
                shortcutModifiers = shortcutModifiers with { Win = true };
            }
            else
            {
                return false;
            }
        }

        if (shortcutModifiers != modifiers)
        {
            return false;
        }

        var keyName = parts[^1];
        return keyName.Length == 1 && char.IsLetterOrDigit(keyName[0])
            ? key == char.ToUpperInvariant(keyName[0])
            : keyName.Equals("Enter", StringComparison.OrdinalIgnoreCase) && key == NativeMethods.VkReturn
                || keyName.Equals("Esc", StringComparison.OrdinalIgnoreCase) && key == NativeMethods.VkEscape;
    }

    private bool HandleImagePreviewShortcut(int key, KeyModifierState modifiers)
    {
        if (!modifiers.Control || modifiers.Alt || modifiers.Win)
        {
            return false;
        }

        switch (key)
        {
            case NativeMethods.VkC:
                if (_imagePreviewItem?.CopyInvoke is null)
                {
                    return false;
                }

                DispatcherQueue.TryEnqueue(() => InvokeImagePreviewCopy(cut: false));
                return true;
            case NativeMethods.VkX:
                if (_imagePreviewItem?.CutInvoke is null)
                {
                    return false;
                }

                DispatcherQueue.TryEnqueue(() => InvokeImagePreviewCopy(cut: true));
                return true;
            case NativeMethods.VkOemPlus:
            case NativeMethods.VkAdd:
                DispatcherQueue.TryEnqueue(() => AdjustImagePreviewZoom(ImagePreviewZoomStep, "ImagePreviewFeedbackZoomIn"));
                return true;
            case NativeMethods.VkOemMinus:
            case NativeMethods.VkSubtract:
                DispatcherQueue.TryEnqueue(() => AdjustImagePreviewZoom(-ImagePreviewZoomStep, "ImagePreviewFeedbackZoomOut"));
                return true;
            case NativeMethods.Vk0:
                DispatcherQueue.TryEnqueue(() => SetImagePreviewZoom(1.0, "ImagePreviewFeedbackZoomReset"));
                return true;
            default:
                return false;
        }
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
        HideImagePreview();
        AddAppTitleItem();
        AddItems(_flyout.Items, _currentItems, parent: null);
    }

    private void AddAppTitleItem()
    {
        _flyout.Items.Add(new MenuFlyoutItem
        {
            Text = AppName,
            IsEnabled = false,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 170, 170, 170)),
            Template = CreateAppTitleTemplate()
        });
        _flyout.Items.Add(new MenuFlyoutSeparator());
    }

    private static ControlTemplate CreateAppTitleTemplate()
    {
        const string xaml = """
            <ControlTemplate
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                TargetType="MenuFlyoutItem">
                <Grid
                    MinHeight="38"
                    Padding="12,0,12,0"
                    Background="{TemplateBinding Background}">
                    <TextBlock
                        VerticalAlignment="Center"
                        Text="{TemplateBinding Text}"
                        Foreground="{TemplateBinding Foreground}"
                        FontWeight="{TemplateBinding FontWeight}"
                        FontSize="13"
                        TextTrimming="CharacterEllipsis" />
                </Grid>
            </ControlTemplate>
            """;
        return (ControlTemplate)XamlReader.Load(xaml);
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

    private void ToggleFocusedImagePreview()
    {
        if (GetFocusedMenuItem() is { } item)
        {
            ToggleImagePreview(item);
        }
    }

    private void ToggleImagePreview(QuickMenuItem item)
    {
        if (_imagePreviewWindow is not null)
        {
            if (!ReferenceEquals(_imagePreviewItem, item))
            {
                HideImagePreview();
                ShowImagePreview(item);
                return;
            }

            HideImagePreview();
            return;
        }

        ShowImagePreview(item);
    }

    private static bool HasImagePreview(QuickMenuItem item)
    {
        return item.PreviewImageBytesProvider is not null || item.IconImageBytes is { Length: > 0 };
    }

    private async void ShowImagePreview(QuickMenuItem? item)
    {
        HideImagePreview();
        var requestId = ++_imagePreviewRequestId;
        var imageBytes = item?.PreviewImageBytesProvider?.Invoke() ?? item?.IconImageBytes;
        if (imageBytes is not { Length: > 0 } || _host.XamlRoot is null)
        {
            return;
        }

        var bitmap = await CreateBitmapImageAsync(imageBytes);
        var previewBackground = new SolidColorBrush(Color.FromArgb(255, 28, 28, 28));
        var panelBackground = new SolidColorBrush(Color.FromArgb(255, 33, 33, 33));
        var imageBackground = new SolidColorBrush(Color.FromArgb(255, 16, 16, 16));
        var foreground = new SolidColorBrush(Color.FromArgb(245, 255, 255, 255));
        var secondaryForeground = new SolidColorBrush(Color.FromArgb(190, 255, 255, 255));
        var image = new Image
        {
            Source = bitmap,
            Stretch = Stretch.Uniform,
            MaxWidth = ImagePreviewBaseImageWidth,
            MaxHeight = ImagePreviewBaseImageHeight
        };
        if (_dismissed || requestId != _imagePreviewRequestId || _host.XamlRoot is null)
        {
            return;
        }

        var frame = new Border
        {
            MaxWidth = ImagePreviewBaseWindowWidth,
            MaxHeight = ImagePreviewBaseWindowHeight,
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(0),
            Background = panelBackground
        };
        var panel = new Grid();
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var imageFrame = new Border
        {
            Background = imageBackground,
            CornerRadius = new CornerRadius(6),
            Child = image
        };
        panel.Children.Add(imageFrame);

        var metaText = new TextBlock
        {
            Text = CreateImagePreviewMetaText(bitmap, item, imageBytes.Length),
            Foreground = secondaryForeground,
            FontSize = 13,
            Margin = new Thickness(0, 10, 0, 10),
            TextWrapping = TextWrapping.NoWrap
        };
        Grid.SetRow(metaText, 1);
        panel.Children.Add(metaText);

        var separator = new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)),
            Margin = new Thickness(0, 0, 0, 10)
        };
        Grid.SetRow(separator, 2);
        panel.Children.Add(separator);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        actions.Children.Add(CreateImagePreviewActionButton("\uE8A7", "Open with default app", () => OpenImagePreviewInDefaultApp(imageBytes)));
        actions.Children.Add(CreateImagePreviewActionButton("\uE77F", "Paste", () =>
        {
            if (_imagePreviewItem is { } previewItem)
            {
                Invoke(previewItem, asPlainText: false);
            }
        }));
        actions.Children.Add(CreateImagePreviewActionButton("\uE8C8", "Copy", () => InvokeImagePreviewCopy(cut: false), item?.CopyInvoke is not null));
        actions.Children.Add(CreateImagePreviewActionButton("\uE74D", "Copy and remove", () => InvokeImagePreviewCopy(cut: true), item?.CutInvoke is not null));
        actions.Children.Add(CreateImagePreviewActionButton("-", "Zoom out", () => AdjustImagePreviewZoom(-ImagePreviewZoomStep, "ImagePreviewFeedbackZoomOut"), fontFamily: "Segoe UI", fontSize: 22));
        actions.Children.Add(CreateImagePreviewActionButton("100%", "Reset zoom", () => SetImagePreviewZoom(1.0, "ImagePreviewFeedbackZoomReset"), fontFamily: "Segoe UI", fontSize: 12));
        actions.Children.Add(CreateImagePreviewActionButton("+", "Zoom in", () => AdjustImagePreviewZoom(ImagePreviewZoomStep, "ImagePreviewFeedbackZoomIn"), fontFamily: "Segoe UI", fontSize: 22));
        Grid.SetRow(actions, 3);
        panel.Children.Add(actions);
        frame.Child = panel;
        var feedbackText = new TextBlock
        {
            Visibility = Visibility.Collapsed,
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Colors.White),
            TextWrapping = TextWrapping.NoWrap
        };
        var feedbackPill = new Border
        {
            Visibility = Visibility.Collapsed,
            Padding = new Thickness(10, 6, 10, 7),
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Color.FromArgb(220, 32, 32, 32)),
            Child = feedbackText,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 14, 14),
            IsHitTestVisible = false
        };
        var previewRoot = new Grid
        {
            Background = previewBackground
        };
        previewRoot.Children.Add(frame);
        previewRoot.Children.Add(feedbackPill);
        AutomationProperties.SetName(frame, _previewImageText);
        AutomationProperties.SetHelpText(frame, "Esc / Ctrl+C / Ctrl+X / Ctrl++ / Ctrl+- / Ctrl+0");
        AutomationProperties.SetName(image, _previewImageText);
        frame.PointerPressed += (_, args) =>
        {
            var point = args.GetCurrentPoint(frame);
            if (!point.Properties.IsLeftButtonPressed || _imagePreviewAppWindow is null)
            {
                return;
            }

            _draggingImagePreview = true;
            _imagePreviewDragged = false;
            _imagePreviewDragCursorOrigin = GetCursorPoint();
            _imagePreviewDragWindowOrigin = _imagePreviewAppWindow.Position;
            frame.CapturePointer(args.Pointer);
            args.Handled = true;
        };
        frame.PointerMoved += (_, args) =>
        {
            if (!_draggingImagePreview || _imagePreviewAppWindow is null)
            {
                return;
            }

            var cursor = GetCursorPoint();
            var deltaX = cursor.X - _imagePreviewDragCursorOrigin.X;
            var deltaY = cursor.Y - _imagePreviewDragCursorOrigin.Y;
            if (Math.Abs(deltaX) > 3 || Math.Abs(deltaY) > 3)
            {
                _imagePreviewDragged = true;
            }

            _imagePreviewAppWindow.Move(new PointInt32(
                _imagePreviewDragWindowOrigin.X + deltaX,
                _imagePreviewDragWindowOrigin.Y + deltaY));
            args.Handled = true;
        };
        frame.PointerReleased += (_, args) =>
        {
            _draggingImagePreview = false;
            frame.ReleasePointerCaptures();
            args.Handled = true;
        };
        frame.Tapped += (_, args) =>
        {
            if (!_imagePreviewDragged && item is not null && !IsFromButton(args.OriginalSource))
            {
                Invoke(item, asPlainText: false);
            }
        };

        var window = new Window
        {
            Title = _previewImageText,
            Content = previewRoot,
            SystemBackdrop = SystemBackdrop
        };
        _imagePreviewWindow = window;
        _imagePreviewItem = item;
        _imagePreviewImage = image;
        _imagePreviewFrame = frame;
        _imagePreviewFeedbackPill = feedbackPill;
        _imagePreviewFeedbackText = feedbackText;
        _imagePreviewZoom = 1.0;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_imagePreviewWindow, window))
            {
                _imagePreviewWindow = null;
                _imagePreviewHwnd = IntPtr.Zero;
                _imagePreviewAppWindow = null;
                _imagePreviewItem = null;
                _imagePreviewImage = null;
                _imagePreviewFrame = null;
                _imagePreviewFeedbackPill = null;
                _imagePreviewFeedbackText = null;
            }
        };
        _openingImagePreview = true;
        window.Activate();
        _imagePreviewHwnd = WindowNative.GetWindowHandle(window);
        ConfigureBorderlessToolWindow(_imagePreviewHwnd);
        _imagePreviewAppWindow = PositionImagePreviewWindow(window);
        ApplyImagePreviewZoom();
        EnqueueAfterDelay(250, () => _openingImagePreview = false);
    }

    private void OpenImagePreviewInDefaultApp(byte[] imageBytes)
    {
        try
        {
            var directory = Path.Combine(Path.GetTempPath(), "Clipton", "ImagePreview");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"preview-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}.png");
            File.WriteAllBytes(path, imageBytes);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path)
            {
                UseShellExecute = true
            });
        }
        catch
        {
        }
    }

    private static Button CreateImagePreviewActionButton(
        string glyph,
        string tooltip,
        Action action,
        bool isEnabled = true,
        string fontFamily = "Segoe Fluent Icons",
        double fontSize = 17)
    {
        var button = new Button
        {
            Width = 42,
            Height = 38,
            Padding = new Thickness(0),
            IsEnabled = isEnabled,
            Background = new SolidColorBrush(Color.FromArgb(255, 43, 43, 43)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 68, 68, 68)),
            Foreground = new SolidColorBrush(Color.FromArgb(245, 255, 255, 255)),
            Content = new FontIcon
            {
                Glyph = glyph,
                FontFamily = new FontFamily(fontFamily),
                FontSize = fontSize
            }
        };
        button.Click += (_, _) => action();
        ToolTipService.SetToolTip(button, tooltip);
        AutomationProperties.SetName(button, tooltip);
        return button;
    }

    private static string CreateImagePreviewMetaText(BitmapImage bitmap, QuickMenuItem? item, int byteLength)
    {
        var dimensions = bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0
            ? $"{bitmap.PixelWidth} x {bitmap.PixelHeight}"
            : item?.Subtitle;
        return string.IsNullOrWhiteSpace(dimensions)
            ? FormatByteSize(byteLength)
            : $"{dimensions}    {FormatByteSize(byteLength)}";
    }

    private static string FormatByteSize(int byteLength)
    {
        return byteLength >= 1024 * 1024
            ? $"{byteLength / 1024d / 1024d:0.0} MB"
            : $"{Math.Max(1, byteLength / 1024)} KB";
    }

    private static bool IsFromButton(object? source)
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

    private void InvokeImagePreviewCopy(bool cut)
    {
        var item = _imagePreviewItem;
        var action = cut ? item?.CutInvoke : item?.CopyInvoke;
        if (action is null)
        {
            return;
        }

        action();
        ShowImagePreviewFeedback(cut ? "ImagePreviewFeedbackCut" : "ImagePreviewFeedbackCopy");
        if (cut)
        {
            EnqueueAfterDelay(450, () =>
            {
                HideImagePreview();
                Dismiss();
            });
        }
    }

    private void AdjustImagePreviewZoom(double delta, string feedbackKey)
    {
        SetImagePreviewZoom(_imagePreviewZoom + delta, feedbackKey);
    }

    private void SetImagePreviewZoom(double zoom, string? feedbackKey = null)
    {
        _imagePreviewZoom = Math.Clamp(zoom, ImagePreviewMinZoom, ImagePreviewMaxZoom);
        ApplyImagePreviewZoom();
        if (feedbackKey is not null)
        {
            ShowImagePreviewFeedback(feedbackKey);
        }
    }

    private void ShowImagePreviewFeedback(string key)
    {
        if (_imagePreviewFeedbackPill is null || _imagePreviewFeedbackText is null)
        {
            return;
        }

        var requestId = ++_imagePreviewFeedbackRequestId;
        _imagePreviewFeedbackText.Text = _previewStrings.TryGetValue(key, out var text) ? text : key;
        _imagePreviewFeedbackText.Visibility = Visibility.Visible;
        _imagePreviewFeedbackPill.Visibility = Visibility.Visible;
        EnqueueAfterDelay(1200, () =>
        {
            if (requestId != _imagePreviewFeedbackRequestId || _imagePreviewFeedbackPill is null || _imagePreviewFeedbackText is null)
            {
                return;
            }

            _imagePreviewFeedbackText.Visibility = Visibility.Collapsed;
            _imagePreviewFeedbackPill.Visibility = Visibility.Collapsed;
        });
    }

    private void ApplyImagePreviewZoom()
    {
        if (_imagePreviewImage is null || _imagePreviewFrame is null || _imagePreviewAppWindow is null)
        {
            return;
        }

        _imagePreviewImage.MaxWidth = ImagePreviewBaseImageWidth * _imagePreviewZoom;
        _imagePreviewImage.MaxHeight = ImagePreviewBaseImageHeight * _imagePreviewZoom;
        _imagePreviewFrame.MaxWidth = ImagePreviewBaseWindowWidth * _imagePreviewZoom;
        _imagePreviewFrame.MaxHeight = ImagePreviewBaseWindowHeight * _imagePreviewZoom;
        var requestedWidth = (int)Math.Round(ImagePreviewBaseWindowWidth * _imagePreviewZoom);
        var requestedHeight = (int)Math.Round(ImagePreviewBaseWindowHeight * _imagePreviewZoom);
        var workingArea = GetWorkingArea(new System.Drawing.Point(
            _imagePreviewAppWindow.Position.X,
            _imagePreviewAppWindow.Position.Y));
        var maxWidth = Math.Max(HostWindowSize, workingArea.Width - ScreenEdgePadding * 2);
        var maxHeight = Math.Max(HostWindowSize, workingArea.Height - ScreenEdgePadding * 2);
        var width = Math.Min(requestedWidth, maxWidth);
        var height = Math.Min(requestedHeight, maxHeight);
        _imagePreviewAppWindow.Resize(new SizeInt32(width, height));
        ConfigureBorderlessToolWindow(_imagePreviewHwnd);
        MovePreviewWindowInsideWorkingArea(width, height);
    }

    private void MovePreviewWindowInsideWorkingArea(int width, int height)
    {
        if (_imagePreviewAppWindow is null)
        {
            return;
        }

        var workingArea = GetWorkingArea(new System.Drawing.Point(
            _imagePreviewAppWindow.Position.X,
            _imagePreviewAppWindow.Position.Y));
        var minX = workingArea.Left + ScreenEdgePadding;
        var minY = workingArea.Top + ScreenEdgePadding;
        var maxX = Math.Max(minX, workingArea.Right - width - ScreenEdgePadding);
        var maxY = Math.Max(minY, workingArea.Bottom - height - ScreenEdgePadding);
        _imagePreviewAppWindow.Move(new PointInt32(
            Math.Clamp(_imagePreviewAppWindow.Position.X, minX, maxX),
            Math.Clamp(_imagePreviewAppWindow.Position.Y, minY, maxY)));
    }

    private void HideImagePreview()
    {
        _imagePreviewRequestId++;
        if (_imagePreviewWindow is { } window)
        {
            _imagePreviewWindow = null;
            _imagePreviewHwnd = IntPtr.Zero;
            _imagePreviewAppWindow = null;
            _imagePreviewItem = null;
            _imagePreviewImage = null;
            _imagePreviewFrame = null;
            _imagePreviewFeedbackPill = null;
            _imagePreviewFeedbackText = null;
            _draggingImagePreview = false;
            window.Close();
        }
    }

    private bool IsInteractionForeground()
    {
        if (!_opened || _dismissed)
        {
            return false;
        }

        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(foreground, out var processId);
        return processId == Environment.ProcessId;
    }

    private static AppWindow? PositionImagePreviewWindow(Window window)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        var id = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(id);
        if (appWindow is null)
        {
            return null;
        }

        const int previewWidth = 560;
        const int previewHeight = 420;
        var point = GetCursorPoint();
        var workingArea = GetWorkingArea(point);
        var x = Math.Clamp(point.X + 18, workingArea.Left + ScreenEdgePadding, workingArea.Right - previewWidth - ScreenEdgePadding);
        var y = Math.Clamp(point.Y + 18, workingArea.Top + ScreenEdgePadding, workingArea.Bottom - previewHeight - ScreenEdgePadding);
        appWindow.Resize(new SizeInt32(previewWidth, previewHeight));
        appWindow.Move(new PointInt32(x, y));
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }

        ConfigureBorderlessToolWindow(hwnd);
        return appWindow;
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
            _noSearchResultsText,
            _previewImageText,
            _previewStrings);
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
                if (HasImagePreview(item))
                {
                    var previewItem = CreateImagePreviewMenuItem(item);
                    optionSubItem.Items.Add(previewItem);
                    _parentItem[previewItem] = optionSubItem;
                    _childFocusableItems[optionSubItem].Add(previewItem);
                    if (item.PasteOptions.Count > 0)
                    {
                        optionSubItem.Items.Add(new MenuFlyoutSeparator());
                    }
                }

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

                ConfigureImagePreviewShortcut(optionSubItem, item);
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

            ConfigureImagePreviewShortcut(flyoutItem, item);
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

    private void ConfigureImagePreviewShortcut(MenuFlyoutItemBase flyoutItem, QuickMenuItem item)
    {
        if (item.PreviewImageBytesProvider is null && item.IconImageBytes is not { Length: > 0 })
        {
            return;
        }

        flyoutItem.KeyDown += (_, args) =>
        {
            if (args.Key != VirtualKey.Space)
            {
                return;
            }

            args.Handled = true;
            ToggleImagePreview(item);
        };
        var accelerator = new KeyboardAccelerator { Key = VirtualKey.Space };
        accelerator.Invoked += (_, args) =>
        {
            args.Handled = true;
            ToggleImagePreview(item);
        };
        flyoutItem.KeyboardAccelerators.Add(accelerator);
    }

    private static void AddFolderPlaceholder(MenuFlyoutSubItem folderItem)
    {
        folderItem.Items.Add(new MenuFlyoutItem
        {
            Text = "Loading...",
            IsEnabled = false,
            Icon = new FontIcon
            {
                Glyph = "\uE895",
                FontFamily = new FontFamily("Segoe Fluent Icons")
            }
        });
    }

    private void EnsureFolderItems(MenuFlyoutSubItem folderItem)
    {
        if (_materializedFolderItems.Contains(folderItem)
            || _materializingFolderItems.Contains(folderItem)
            || folderItem.Tag is not QuickMenuItem item)
        {
            return;
        }

        _materializingFolderItems.Add(folderItem);
        folderItem.Items.Clear();
        AddFolderPlaceholder(folderItem);
        _childFocusableItems[folderItem] = [];
        _ = Task.Run(item.GetChildren).ContinueWith(task =>
        {
            var children = task.Status == TaskStatus.RanToCompletion ? task.Result : [];
            DispatcherQueue.TryEnqueue(() => CompleteFolderItems(folderItem, children));
        });
    }

    private void CompleteFolderItems(MenuFlyoutSubItem folderItem, IReadOnlyList<QuickMenuItem> children)
    {
        _materializingFolderItems.Remove(folderItem);
        if (_dismissed || _materializedFolderItems.Contains(folderItem))
        {
            return;
        }

        _materializedFolderItems.Add(folderItem);
        folderItem.Items.Clear();
        _childFocusableItems[folderItem] = [];
        AddItems(folderItem.Items, children, folderItem);
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
        foreach (var optionItem in pasteOptionsFlyout.Items.OfType<MenuFlyoutItem>())
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

        if (HasImagePreview(item))
        {
            flyout.Items.Add(CreateImagePreviewMenuItem(item));
            if (item.PasteOptions is { Count: > 0 })
            {
                flyout.Items.Add(new MenuFlyoutSeparator());
            }
        }

        foreach (var option in item.PasteOptions ?? [])
        {
            flyout.Items.Add(CreatePasteOptionMenuItem(option));
        }

        return flyout;
    }

    private MenuFlyoutItem CreateImagePreviewMenuItem(QuickMenuItem item)
    {
        var previewItem = new MenuFlyoutItem
        {
            Text = _previewImageText,
            Icon = new FontIcon
            {
                Glyph = "\uE890",
                FontFamily = new FontFamily("Segoe Fluent Icons")
            },
            Tag = new QuickMenuImagePreviewCommand(item),
            KeyboardAcceleratorTextOverride = "Space"
        };
        if (item.IconImageBytes is { Length: > 0 } imageBytes)
        {
            previewItem.Loaded += async (_, _) => await InsertImagePreviewAsync(previewItem, imageBytes, "small");
        }

        previewItem.Click += (_, _) => ToggleImagePreview(item);
        return previewItem;
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

    private QuickMenuImagePreviewCommand? GetFocusedImagePreviewCommand()
    {
        if (GetTrackedFocusedMenuItemBase() is { Tag: QuickMenuImagePreviewCommand trackedCommand })
        {
            return trackedCommand;
        }

        if (_host.XamlRoot is not null
            && FocusManager.GetFocusedElement(_host.XamlRoot) is MenuFlyoutItem { Tag: QuickMenuImagePreviewCommand focusedCommand })
        {
            return focusedCommand;
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

    private static async Task<BitmapImage> CreateBitmapImageAsync(byte[] bytes, int decodePixelWidth = 0)
    {
        var bitmap = new BitmapImage();
        if (decodePixelWidth > 0)
        {
            bitmap.DecodePixelWidth = decodePixelWidth;
        }

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

    private static void ConfigureBorderlessToolWindow(IntPtr hwnd)
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

        var borderColor = DwmwaColorNone;
        _ = DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref borderColor, sizeof(int));

        var ncRenderingPolicy = DwmncrpDisabled;
        _ = DwmSetWindowAttribute(hwnd, DwmwaNcRenderingPolicy, ref ncRenderingPolicy, sizeof(int));
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hwnd,
        IntPtr hwndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint flags);

    [DllImport("dwmapi.dll", SetLastError = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);
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
    Func<byte[]?>? PreviewImageBytesProvider = null,
    Action? CopyInvoke = null,
    Action? CutInvoke = null,
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

internal sealed record QuickMenuImagePreviewCommand(QuickMenuItem Item);

internal readonly record struct KeyModifierState(bool Control, bool Shift, bool Alt, bool Win)
{
    public static KeyModifierState None { get; } = new(false, false, false, false);
}

internal sealed record RootFlyoutPlacement(
    System.Drawing.Point Anchor,
    FlyoutPlacementMode Placement);

internal sealed record SearchPromptResult(string? Query, bool OpenDetailedSearch);
