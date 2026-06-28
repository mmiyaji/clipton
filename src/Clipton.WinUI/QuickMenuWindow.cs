using System.Runtime.InteropServices;
using System.Globalization;
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
    private const int InitialShowFlyoutDelayMilliseconds = 120;
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
    private const int ImagePreviewTempMaxFiles = 20;
    private static readonly TimeSpan ImagePreviewTempMaxAge = TimeSpan.FromHours(1);
    private static readonly TimeSpan ImagePreviewTempDeleteDelay = TimeSpan.FromMinutes(10);
    private static readonly NativeMethods.LowLevelKeyboardProc s_keyboardProc = OnStaticKeyboardHook;
    private static readonly NativeMethods.LowLevelMouseProc s_mouseProc = OnStaticMouseHook;
    private static readonly Lazy<ControlTemplate> s_appTitleTemplate = new(CreateAppTitleTemplate);
    private static QuickMenuWindow? s_activeWindow;
    private static IntPtr s_keyboardHook;
    private static IntPtr s_mouseHook;
    private static long s_lastNativeResourceCollectionTick;
    private IReadOnlyList<QuickMenuItem> _rootItems = [];
    private readonly string _title;
    private readonly Grid _host = new();
    private readonly MenuFlyout _flyout = new();
    private readonly QuickMenuThemePalette _palette;
    private readonly string _previewImageText;
    private readonly IReadOnlyDictionary<string, string> _previewStrings;
    private readonly string _imagePreviewSize;
    private readonly Action _openSearch;
    private readonly QuickMenuShortcutSettings _shortcuts;
    private readonly string _pasteOptionsHelpText;
    private readonly CultureInfo _culture;
    private bool _showShortcutHints;
    private readonly List<MenuFlyoutItemBase> _rootFocusableItems = [];
    private readonly Dictionary<MenuFlyoutItemBase, MenuFlyout> _pasteOptionsFlyouts = [];
    private readonly HashSet<MenuFlyoutSubItem> _materializedFolderItems = [];
    private readonly HashSet<MenuFlyoutSubItem> _materializingFolderItems = [];
    private AppWindow? _appWindow;
    private IntPtr _hwnd;
    private bool _dismissed;
    private bool _opened;
    private bool _reopening;
    private bool _hostLoaded;
    private bool _initialShowPending = true;
    private bool _showFlyoutScheduled;
    private long _focusToken;
    private IReadOnlyList<QuickMenuItem> _currentItems;
    private bool _revealMaskedItems;
    private bool _showCapturedAt;
    private MenuFlyoutItemBase? _hoveredPasteOptionsItem;
    private MenuFlyoutItemBase? _trackedFocusedItem;
    private object? _trackedFocusedTag;
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
        Action openSearch,
        string previewImageText,
        string pasteOptionsHelpText,
        IReadOnlyDictionary<string, string> previewStrings,
        CultureInfo culture)
    {
        _title = title;
        _rootItems = items;
        _currentItems = items;
        _palette = QuickMenuThemePalette.ForTheme(theme);
        _imagePreviewSize = NormalizeImagePreviewSize(imagePreviewSize);
        _showCapturedAt = showCapturedAt;
        _showShortcutHints = showShortcutHints;
        _shortcuts = shortcuts;
        _openSearch = openSearch;
        _previewImageText = previewImageText;
        _pasteOptionsHelpText = pasteOptionsHelpText;
        _previewStrings = previewStrings;
        _culture = culture;
        Title = "Clipton";
        BuildHost();
        BuildFlyout();
        PositionNearCursor();
        _flyout.Closed += (_, _) =>
        {
            if (_openingImagePreview || _reopening)
            {
                return;
            }

            Dismiss();
        };
    }

    public event EventHandler? Dismissed;

    public string DisplayMode => "default";

    public bool IsDismissed => _dismissed;

    public void FocusMenu()
    {
        Activate();
        FocusHostWindow();
        DispatcherQueue.TryEnqueue(() =>
        {
            FocusHostWindow();
            if (_hostLoaded)
            {
                QueueShowFlyout(_initialShowPending ? InitialShowFlyoutDelayMilliseconds : 0);
            }
        });
    }

    public void Reopen(IReadOnlyList<QuickMenuItem> items)
    {
        // Hiding the flyout raises Closed, which must not dismiss the menu
        // that is being rebuilt right here.
        _reopening = true;
        UninstallKeyboardHook();
        UninstallMouseHook();
        _flyout.Hide();
        _appWindow?.Hide();
        ReleaseMenuReferences();
        _dismissed = false;
        _opened = false;
        _initialShowPending = false;
        _showFlyoutScheduled = false;
        _rootItems = items;
        _currentItems = items;
        BuildFlyout();
        PositionNearCursor();
        FocusMenu();
        EnqueueAfterDelay(400, () => _reopening = false);
    }

    public void UpdateDisplayOptions(bool showCapturedAt, bool showShortcutHints)
    {
        if (_showCapturedAt == showCapturedAt && _showShortcutHints == showShortcutHints)
        {
            return;
        }

        _showCapturedAt = showCapturedAt;
        _showShortcutHints = showShortcutHints;
        if (!_dismissed)
        {
            RefreshVisibleItems(_flyout.Items);
        }
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
            if (!_dismissed)
            {
                // Reopened before the cleanup ran; the new menu must survive.
                return;
            }

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
        _pasteOptionsFlyouts.Clear();
        _materializedFolderItems.Clear();
        _materializingFolderItems.Clear();
        _hoveredPasteOptionsItem = null;
        _trackedFocusedItem = null;
        _trackedFocusedTag = null;
        _currentItems = [];
        _rootItems = [];
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
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, blocking: false, compacting: false);
    }

    private void BuildHost()
    {
        _host.RequestedTheme = _palette.RequestedTheme;
        _host.Background = new SolidColorBrush(Colors.Transparent);
        _host.IsTabStop = true;
        _host.Loaded += (_, _) =>
        {
            _hostLoaded = true;
            QueueShowFlyout(_initialShowPending ? InitialShowFlyoutDelayMilliseconds : 0);
        };
        Content = _host;
    }

    private void QueueShowFlyout(int delayMilliseconds)
    {
        if (_showFlyoutScheduled || _opened || _dismissed)
        {
            return;
        }

        _showFlyoutScheduled = true;
        if (delayMilliseconds <= 0)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                _showFlyoutScheduled = false;
                ShowFlyout();
            });
            return;
        }

        EnqueueAfterDelay(delayMilliseconds, () =>
        {
            _showFlyoutScheduled = false;
            ShowFlyout();
        });
    }

    private void ShowFlyout()
    {
        if (_opened || _dismissed || _host.XamlRoot is null)
        {
            return;
        }

        _opened = true;
        _initialShowPending = false;
        InstallKeyboardHook();
        InstallMouseHook();
        FocusHostWindow();
        _flyout.ShowAt(_host, new FlyoutShowOptions
        {
            Placement = _flyout.Placement,
            Position = new Windows.Foundation.Point(0, 0)
        });
        var focusToken = ++_focusToken;
        FocusRootItemAfterDelay(focusToken, 0);
        FocusRootItemAfterDelay(focusToken, 80);
        FocusRootItemAfterDelay(focusToken, 180);
        FocusRootItemAfterDelay(focusToken, 320);
        FocusRootItemAfterDelay(focusToken, 500);
        FocusRootItemAfterDelay(focusToken, 700);
    }

    private void FocusHostWindow()
    {
        if (_hwnd != IntPtr.Zero)
        {
            NativeMethods.SetForegroundWindow(_hwnd);
        }

        _host.Focus(FocusState.Programmatic);
    }

    private void FocusRootItemAfterDelay(long focusToken, int delayMilliseconds)
    {
        _ = Task.Delay(delayMilliseconds).ContinueWith(_ => DispatcherQueue.TryEnqueue(() =>
        {
            if (_dismissed
                || _focusToken != focusToken
                || GetFocusedMenuElement().Element is not null)
            {
                return;
            }

            FocusHostWindow();
            FocusFirstRootItem();
        }));
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
        if (targetItem is not { Tag: QuickMenuItem item } flyoutItem || !item.HasPasteOptions)
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

        if (modifiers == KeyModifierState.None && TryGetNumberShortcutIndex(key, out var numberShortcutIndex))
        {
            DispatcherQueue.TryEnqueue(() => InvokeNumberShortcut(numberShortcutIndex));
            return 1;
        }

        if (modifiers == KeyModifierState.None && key == NativeMethods.VkE)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (GetFocusedMenuItem() is { } item)
                {
                    InvokeEdit(item);
                }
            });
            return 1;
        }

        switch (key)
        {
            case NativeMethods.VkReturn:
                if (_imagePreviewWindow is not null)
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        if (_imagePreviewItem is { } previewedItem)
                        {
                            Invoke(previewedItem, asPlainText: false);
                        }
                    });
                }
                else if (GetFocusedPasteOption() is { } focusedPasteOption)
                {
                    DispatcherQueue.TryEnqueue(() => InvokePasteOption(focusedPasteOption));
                }
                else if (GetFocusedPasteOptionsSubItem() is { } subItemQuickItem)
                {
                    // In-folder items with paste options are MenuFlyoutSubItems;
                    // native Enter would only open the submenu. Paste directly
                    // instead, matching root-level items.
                    DispatcherQueue.TryEnqueue(() => Invoke(subItemQuickItem, asPlainText: false));
                }
                else if (GetFocusedInvokableMenuItem() is { } focusedQuickItem)
                {
                    DispatcherQueue.TryEnqueue(() => Invoke(focusedQuickItem, asPlainText: false));
                }
                else if (GetFirstRootInvokableMenuItem() is { } fallbackQuickItem)
                {
                    DispatcherQueue.TryEnqueue(() => Invoke(fallbackQuickItem, asPlainText: false));
                }
                else
                {
                    handled = false;
                }
                break;
            case NativeMethods.VkSpace:
                if (GetFocusedMenuItem() is { } previewItem && HasImagePreview(previewItem))
                {
                    DispatcherQueue.TryEnqueue(() => ToggleImagePreview(previewItem));
                }
                else if (GetFocusedMenuItem() is { PreviewInvoke: not null } textPreviewItem)
                {
                    DispatcherQueue.TryEnqueue(() => InvokePreview(textPreviewItem));
                }
                else if (GetFocusedImagePreviewCommand() is { } previewCommand)
                {
                    DispatcherQueue.TryEnqueue(() => ToggleImagePreview(previewCommand.Item));
                }
                else
                {
                    handled = false;
                }
                break;
            case NativeMethods.VkRight:
                // Submenu presenters can consume Right before it reaches the
                // item-level KeyDown handler, so paste options are opened from
                // the hook; folders stay unhandled for the native submenu open.
                if (GetFocusedPasteOptionsItem() is { Tag: QuickMenuItem optionsQuickItem } optionsItem)
                {
                    DispatcherQueue.TryEnqueue(() => ShowPasteOptionsForItem(optionsItem, EnsurePasteOptionsFlyout(optionsItem, optionsQuickItem), focusOptions: true));
                }
                else
                {
                    handled = false;
                }
                break;
            case NativeMethods.VkEscape:
                if (_imagePreviewWindow is not null)
                {
                    DispatcherQueue.TryEnqueue(HideImagePreviewAndMaybeDismiss);
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

    private static bool TryGetNumberShortcutIndex(int key, out int index)
    {
        if (key is >= 0x31 and <= 0x39)
        {
            index = key - 0x31;
            return true;
        }

        if (key is >= 0x61 and <= 0x69)
        {
            index = key - 0x61;
            return true;
        }

        index = -1;
        return false;
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
                        flyoutItem.KeyboardAcceleratorTextOverride = item.HasPasteOptions
                            ? BuildPasteOptionsHint(item)
                            : BuildCommandHint(item);
                    }

                    break;
            }
        }
    }

    private void BuildFlyout()
    {
        _flyout.Items.Clear();
        _rootFocusableItems.Clear();
        _pasteOptionsFlyouts.Clear();
        _materializedFolderItems.Clear();
        _materializingFolderItems.Clear();
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
            Foreground = _palette.AppTitleForeground.ToBrush(),
            Template = s_appTitleTemplate.Value
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

            HideImagePreviewAndMaybeDismiss();
            return;
        }

        ShowImagePreview(item);
    }

    // When the preview was opened by natively invoking the preview command the
    // menu chain is already closed; closing the preview must end the session
    // instead of leaving an invisible window holding the keyboard hook.
    private void HideImagePreviewAndMaybeDismiss()
    {
        HideImagePreview();
        if (!_dismissed && !_flyout.IsOpen)
        {
            Dismiss();
        }
    }

    private static bool HasImagePreview(QuickMenuItem item)
    {
        return item.PreviewImageBytesProvider is not null || item.IconImageBytes is { Length: > 0 };
    }

    private async void ShowImagePreview(QuickMenuItem? item)
    {
        try
        {
            await ShowImagePreviewAsync(item);
        }
        catch (Exception exception)
        {
            AppDiagnostics.Log(exception, "Quick menu image preview");
        }
    }

    private async Task ShowImagePreviewAsync(QuickMenuItem? item)
    {
        HideImagePreview();
        var requestId = ++_imagePreviewRequestId;
        var imageBytes = item?.PreviewImageBytesProvider?.Invoke() ?? item?.IconImageBytes;
        if (imageBytes is not { Length: > 0 } || _host.XamlRoot is null)
        {
            return;
        }

        // The guard must be up before the first await: invoking the preview
        // command natively closes the cascading menu chain, and the resulting
        // flyout Closed event would otherwise dismiss this window mid-load.
        _openingImagePreview = true;
        var bitmap = await CreateBitmapImageAsync(imageBytes);
        var previewBackground = _palette.ImagePreviewWindowBackground.ToBrush();
        var panelBackground = _palette.ImagePreviewPanelBackground.ToBrush();
        var imageBackground = _palette.ImagePreviewImageBackground.ToBrush();
        var secondaryForeground = _palette.ImagePreviewSecondaryForeground.ToBrush();
        var image = new Image
        {
            Source = bitmap,
            Stretch = Stretch.Uniform,
            MaxWidth = ImagePreviewBaseImageWidth,
            MaxHeight = ImagePreviewBaseImageHeight
        };
        if (_dismissed || requestId != _imagePreviewRequestId || _host.XamlRoot is null)
        {
            _openingImagePreview = false;
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
            Background = _palette.ImagePreviewSeparator.ToBrush(),
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
        actions.Children.Add(CreateImagePreviewActionButton("\uE8A7", GetPreviewString("ImagePreviewOpenDefaultApp"), () => OpenImagePreviewInDefaultApp(imageBytes)));
        actions.Children.Add(CreateImagePreviewActionButton("\uE77F", GetPreviewString("Paste"), () =>
        {
            if (_imagePreviewItem is { } previewItem)
            {
                Invoke(previewItem, asPlainText: false);
            }
        }));
        actions.Children.Add(CreateImagePreviewActionButton("\uE8C8", GetPreviewString("Copy"), () => InvokeImagePreviewCopy(cut: false), item?.CopyInvoke is not null));
        actions.Children.Add(CreateImagePreviewActionButton("\uE74D", GetPreviewString("CopyAndRemove"), () => InvokeImagePreviewCopy(cut: true), item?.CutInvoke is not null));
        actions.Children.Add(CreateImagePreviewActionButton("-", GetPreviewString("ZoomOut"), () => AdjustImagePreviewZoom(-ImagePreviewZoomStep, "ImagePreviewFeedbackZoomOut"), fontFamily: "Segoe UI", fontSize: 22));
        actions.Children.Add(CreateImagePreviewActionButton("100%", GetPreviewString("ResetZoom"), () => SetImagePreviewZoom(1.0, "ImagePreviewFeedbackZoomReset"), fontFamily: "Segoe UI", fontSize: 12));
        actions.Children.Add(CreateImagePreviewActionButton("+", GetPreviewString("ZoomIn"), () => AdjustImagePreviewZoom(ImagePreviewZoomStep, "ImagePreviewFeedbackZoomIn"), fontFamily: "Segoe UI", fontSize: 22));
        actions.Children.Add(CreateImagePreviewActionButton("\uE711", GetPreviewString("Close"), HideImagePreview));
        Grid.SetRow(actions, 3);
        panel.Children.Add(actions);
        frame.Child = panel;
        var feedbackText = new TextBlock
        {
            Visibility = Visibility.Collapsed,
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = _palette.ImagePreviewFeedbackForeground.ToBrush(),
            TextWrapping = TextWrapping.NoWrap
        };
        var feedbackPill = new Border
        {
            Visibility = Visibility.Collapsed,
            Padding = new Thickness(10, 6, 10, 7),
            CornerRadius = new CornerRadius(6),
            Background = _palette.ImagePreviewFeedbackBackground.ToBrush(),
            Child = feedbackText,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 14, 14),
            IsHitTestVisible = false
        };
        var previewRoot = new Grid
        {
            Background = previewBackground,
            RequestedTheme = _palette.RequestedTheme
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
                if (!_dismissed && !_flyout.IsOpen)
                {
                    Dismiss();
                }
            }
        };
        _openingImagePreview = true;
        // Size, position and chrome must be final before Activate; otherwise a
        // default-sized titled window flashes briefly on screen.
        _imagePreviewHwnd = WindowNative.GetWindowHandle(window);
        ConfigureBorderlessToolWindow(_imagePreviewHwnd);
        _imagePreviewAppWindow = PositionImagePreviewWindow(window);
        window.Activate();
        ApplyImagePreviewZoom();
        EnqueueAfterDelay(250, () => _openingImagePreview = false);
    }

    private void OpenImagePreviewInDefaultApp(byte[] imageBytes)
    {
        _ = OpenImagePreviewInDefaultAppAsync(imageBytes);
    }

    private async Task OpenImagePreviewInDefaultAppAsync(byte[] imageBytes)
    {
        try
        {
            var directory = GetImagePreviewTempDirectory();
            CleanupImagePreviewTempFiles(directory);
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"preview-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}.png");
            File.WriteAllBytes(path, imageBytes);
            await ExternalLauncher.OpenFileAsync(path);
            ScheduleImagePreviewTempFileDeletion(path);
        }
        catch
        {
        }
    }

    internal static void CleanupImagePreviewTempFiles(bool deleteAll = false)
    {
        CleanupImagePreviewTempFiles(GetImagePreviewTempDirectory(), deleteAll);
    }

    private static string GetImagePreviewTempDirectory()
    {
        return Path.Combine(Path.GetTempPath(), "Clipton", "ImagePreview");
    }

    private static void CleanupImagePreviewTempFiles(string directoryPath, bool deleteAll = false)
    {
        try
        {
            var directory = new DirectoryInfo(directoryPath);
            if (!directory.Exists)
            {
                return;
            }

            var cutoff = DateTime.UtcNow - ImagePreviewTempMaxAge;
            var currentFiles = new List<FileInfo>();
            foreach (var file in directory.EnumerateFiles("preview-*.png"))
            {
                if (deleteAll || file.CreationTimeUtc < cutoff)
                {
                    TryDeleteFile(file);
                }
                else
                {
                    currentFiles.Add(file);
                }
            }

            if (currentFiles.Count <= ImagePreviewTempMaxFiles)
            {
                return;
            }

            foreach (var file in currentFiles
                .OrderByDescending(file => file.CreationTimeUtc)
                .Skip(ImagePreviewTempMaxFiles))
            {
                TryDeleteFile(file);
            }
        }
        catch
        {
        }
    }

    private static void ScheduleImagePreviewTempFileDeletion(string path)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(ImagePreviewTempDeleteDelay);
                TryDeleteFile(path);
            }
            catch
            {
            }
        });
    }

    private static void TryDeleteFile(FileInfo file)
    {
        try
        {
            file.Delete();
        }
        catch
        {
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
        }
    }

    private Button CreateImagePreviewActionButton(
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
            Background = _palette.ImagePreviewActionBackground.ToBrush(),
            BorderBrush = _palette.ImagePreviewActionBorder.ToBrush(),
            Foreground = _palette.ImagePreviewActionForeground.ToBrush(),
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
        _imagePreviewFeedbackText.Text = GetPreviewString(key);
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

    private void SearchMenu()
    {
        Dismiss();
        _openSearch();
    }

    internal static IEnumerable<QuickMenuItem> FlattenSearchableItems(IEnumerable<QuickMenuItem> items)
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

    internal static bool MatchesSearch(SearchFilter filter, QuickMenuItem item)
    {
        if (filter.IsEmpty)
        {
            return true;
        }

        if (!filter.MatchesDate(item.CapturedAt ?? DateTimeOffset.Now)
            || !filter.MatchesPinned(item.IsPinned)
            || !filter.MatchesType(item.Formats ?? []))
        {
            return false;
        }

        if (filter.HasUrl is not null
            && !filter.MatchesUrl(CliptonRuntime.ExtractUrls($"{item.Title} {item.Subtitle} {item.RevealedTitle}").Length > 0))
        {
            return false;
        }

        return filter.MatchesText(() => $"{item.Title} {item.Subtitle} {item.KindLabel} {item.RevealedTitle}");
    }

    private void AddItems(IList<MenuFlyoutItemBase> target, IReadOnlyList<QuickMenuItem> items, MenuFlyoutSubItem? parent)
    {
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
                subItem.Tag = item;
                ApplyMenuItemAutomation(subItem, item, opensPasteOptions: false);
                if (parent is null)
                {
                    _rootFocusableItems.Add(subItem);
                }

                AddFolderPlaceholder(subItem);
                subItem.PointerEntered += (_, _) => EnsureFolderItems(subItem);
                subItem.GotFocus += (_, _) => EnsureFolderItems(subItem);
                TrackFocus(subItem, item);
                target.Add(subItem);
                continue;
            }

            // Inside folders, paste options must be a native cascading submenu:
            // showing a separate flyout from within a submenu light-dismisses
            // the whole menu chain. The chevron is swapped for the More glyph
            // so it stays visually distinct from folder navigation, and Enter
            // is overridden in the keyboard hook to paste directly.
            if (parent is not null && item.HasPasteOptions)
            {
                var pasteOptions = item.GetPasteOptions();
                var optionSubItem = new MenuFlyoutSubItem
                {
                    Text = BuildDisplayText(item),
                    Icon = CreateIcon(item),
                    Tag = item
                };
                ApplyMenuItemAutomation(optionSubItem, item, opensPasteOptions: true);
                if (HasImagePreview(item))
                {
                    optionSubItem.Items.Add(CreateImagePreviewMenuItem(item));
                    if (pasteOptions.Count > 0)
                    {
                        optionSubItem.Items.Add(new MenuFlyoutSeparator());
                    }
                }

                foreach (var option in pasteOptions)
                {
                    optionSubItem.Items.Add(CreatePasteOptionMenuItem(option));
                }

                optionSubItem.Loaded += (_, _) =>
                {
                    ReplaceSubItemChevronWithMoreGlyph(optionSubItem);
                    if (!string.Equals(_imagePreviewSize, "none", StringComparison.OrdinalIgnoreCase)
                        && item.IconImageBytes is { Length: > 0 })
                    {
                        _ = InsertImagePreviewAsync(optionSubItem, item.IconImageBytes, _imagePreviewSize, _palette);
                    }
                };
                ConfigureImagePreviewShortcut(optionSubItem, item);
                TrackFocus(optionSubItem, item);
                target.Add(optionSubItem);
                continue;
            }

            var flyoutItem = new MenuFlyoutItem
            {
                Text = BuildDisplayText(item),
                KeyboardAcceleratorTextOverride = item.HasPasteOptions
                    ? BuildPasteOptionsHint(item)
                    : BuildCommandHint(item),
                Icon = CreateIcon(item)
            };
            ApplyMenuItemAutomation(flyoutItem, item, item.HasPasteOptions);
            if (item.HasPasteOptions)
            {
                flyoutItem.KeyDown += (_, args) =>
                {
                    if (args.Key != VirtualKey.Right)
                    {
                        return;
                    }

                    args.Handled = true;
                    ShowPasteOptionsForItem(flyoutItem, EnsurePasteOptionsFlyout(flyoutItem, item), focusOptions: true);
                };
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
                flyoutItem.Loaded += async (_, _) => await InsertImagePreviewAsync(flyoutItem, item.IconImageBytes, _imagePreviewSize, _palette);
            }

            ConfigureImagePreviewShortcut(flyoutItem, item);
            if (parent is null)
            {
                _rootFocusableItems.Add(flyoutItem);
            }

            flyoutItem.Tag = item;
            flyoutItem.Click += (_, _) => Invoke(item, asPlainText: false);
            TrackFocus(flyoutItem, item);
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
    }

    private void AddFolderPlaceholder(MenuFlyoutSubItem folderItem)
    {
        folderItem.Items.Add(new MenuFlyoutItem
        {
            Text = GetPreviewString("QuickMenuFolderLoading"),
            IsEnabled = false,
            Icon = new FontIcon
            {
                Glyph = "\uE895",
                FontFamily = new FontFamily("Segoe Fluent Icons")
            }
        });
    }

    private void AddFolderUnavailablePlaceholder(MenuFlyoutSubItem folderItem)
    {
        folderItem.Items.Add(new MenuFlyoutItem
        {
            Text = GetPreviewString("QuickMenuFolderNoItems"),
            IsEnabled = false,
            Icon = new FontIcon
            {
                Glyph = "\uE783",
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
        _ = Task.Run(item.GetChildren).ContinueWith(task =>
        {
            if (task.Exception is not null)
            {
                AppDiagnostics.Log(task.Exception, $"Quick menu folder load failed. folder={item.Title}");
            }

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

        var submenuIsOpen = folderItem.Items.Any(existing => existing.IsLoaded);
        folderItem.Items.Clear();
        if (children.Count == 0)
        {
            AddFolderUnavailablePlaceholder(folderItem);
            return;
        }

        _materializedFolderItems.Add(folderItem);
        AddItems(folderItem.Items, children, folderItem);
        if (submenuIsOpen)
        {
            FocusWhenLoaded(FirstFocusableItem(folderItem.Items));
        }
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
        foreach (var optionItem in pasteOptionsFlyout.Items.OfType<MenuFlyoutItem>())
        {
            optionItem.KeyDown += (_, args) =>
            {
                if (args.Key != VirtualKey.Left)
                {
                    return;
                }

                args.Handled = true;
                pasteOptionsFlyout.Hide();
                flyoutItem.Focus(FocusState.Keyboard);
            };
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
            if (item.GetPasteOptions().Count > 0)
            {
                flyout.Items.Add(new MenuFlyoutSeparator());
            }
        }

        foreach (var option in item.GetPasteOptions())
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
        TrackFocus(previewItem, previewItem.Tag);
        if (item.IconImageBytes is { Length: > 0 } imageBytes)
        {
            previewItem.Loaded += async (_, _) => await InsertImagePreviewAsync(previewItem, imageBytes, "small", _palette);
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
        AutomationProperties.SetName(optionItem, option.Text);
        TrackFocus(optionItem, option);
        return optionItem;
    }

    private void ApplyMenuItemAutomation(MenuFlyoutItemBase flyoutItem, QuickMenuItem item, bool opensPasteOptions)
    {
        AutomationProperties.SetName(flyoutItem, BuildDisplayText(item));
        var helpText = BuildMenuItemHelpText(item, opensPasteOptions);
        if (!string.IsNullOrWhiteSpace(helpText))
        {
            AutomationProperties.SetHelpText(flyoutItem, helpText);
        }
    }

    private string BuildMenuItemHelpText(QuickMenuItem item, bool opensPasteOptions)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(item.Subtitle))
        {
            parts.Add(item.Subtitle);
        }

        if (opensPasteOptions && !string.IsNullOrWhiteSpace(_pasteOptionsHelpText))
        {
            parts.Add(_pasteOptionsHelpText);
        }

        return string.Join(" ", parts.Distinct(StringComparer.Ordinal));
    }

    private string GetPreviewString(string key)
    {
        return _previewStrings.TryGetValue(key, out var text) ? text : key;
    }

    private void ShowPasteOptionsForItem(MenuFlyoutItemBase flyoutItem, MenuFlyout pasteOptionsFlyout, bool focusOptions)
    {
        flyoutItem.Focus(FocusState.Programmatic);
        pasteOptionsFlyout.ShowAt(flyoutItem);
        if (focusOptions)
        {
            FocusWhenLoaded(FirstFocusableItem(pasteOptionsFlyout.Items));
        }
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

        var capturedAtText = capturedAt.LocalDateTime.ToString("g", _culture);
        return string.IsNullOrWhiteSpace(commandHint)
            ? capturedAtText
            : $"{commandHint}  |  {capturedAtText}";
    }

    private string BuildPasteOptionsHint(QuickMenuItem item)
    {
        var hint = BuildCommandHint(item);
        return string.IsNullOrWhiteSpace(hint) ? "..." : $"{hint}  ...";
    }

    // Menu popups (submenus, paste-option flyouts) have their own XamlRoots,
    // so FocusManager.GetFocusedElement(_host.XamlRoot) cannot see focus inside
    // them. GotFocus/LostFocus tracking covers every level; the tag is cached
    // separately because the keyboard hook thread must not touch
    // DependencyObject properties.
    private void TrackFocus(MenuFlyoutItemBase item, object? tag)
    {
        item.GotFocus += (_, _) =>
        {
            _trackedFocusedItem = item;
            _trackedFocusedTag = tag;
        };
        item.LostFocus += (_, _) =>
        {
            if (ReferenceEquals(_trackedFocusedItem, item))
            {
                _trackedFocusedItem = null;
                _trackedFocusedTag = null;
            }
        };
    }

    private (MenuFlyoutItemBase? Element, object? Tag) GetFocusedMenuElement()
    {
        if (_trackedFocusedItem is { } tracked)
        {
            return (tracked, _trackedFocusedTag);
        }

        try
        {
            if (_host.XamlRoot is not null
                && FocusManager.GetFocusedElement(_host.XamlRoot) is MenuFlyoutItemBase element)
            {
                return (element, element.Tag);
            }
        }
        catch (COMException)
        {
        }

        return (null, null);
    }

    private QuickMenuItem? GetFocusedMenuItem()
    {
        return GetFocusedMenuElement().Tag as QuickMenuItem;
    }

    private QuickMenuItem? GetFocusedInvokableMenuItem()
    {
        return GetFocusedMenuItem() is { IsFolder: false, Invoke: not null } item
            ? item
            : null;
    }

    private MenuFlyoutItem? GetFocusedPasteOptionsItem()
    {
        var (element, tag) = GetFocusedMenuElement();
        return element is MenuFlyoutItem flyoutItem && tag is QuickMenuItem item && item.HasPasteOptions
            ? flyoutItem
            : null;
    }

    private QuickMenuImagePreviewCommand? GetFocusedImagePreviewCommand()
    {
        return GetFocusedMenuElement().Tag as QuickMenuImagePreviewCommand;
    }

    private QuickMenuPasteOption? GetFocusedPasteOption()
    {
        return GetFocusedMenuElement().Tag as QuickMenuPasteOption;
    }

    private QuickMenuItem? GetFocusedPasteOptionsSubItem()
    {
        var (element, tag) = GetFocusedMenuElement();
        return element is MenuFlyoutSubItem && tag is QuickMenuItem item && item.HasPasteOptions && !item.IsFolder
            ? item
            : null;
    }

    // Distinguishes paste options from folder navigation: the native cascading
    // chevron is replaced with the More (...) glyph.
    private static void ReplaceSubItemChevronWithMoreGlyph(MenuFlyoutSubItem subItem)
    {
        var chevron = FindDescendant<FontIcon>(subItem, "SubItemChevron") ?? FindChevronIcon(subItem);
        if (chevron is not null)
        {
            chevron.Glyph = "";
            chevron.FontSize = 14;
        }
    }

    private static FontIcon? FindChevronIcon(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FontIcon { Glyph: "" } icon)
            {
                return icon;
            }

            if (FindChevronIcon(child) is { } descendant)
            {
                return descendant;
            }
        }

        return null;
    }

    private void FocusFirstRootItem()
    {
        FocusWhenLoaded(FirstFocusableItem(_rootFocusableItems));
    }

    private QuickMenuItem? GetFirstRootInvokableMenuItem()
    {
        return _rootFocusableItems
            .Select(item => item.Tag as QuickMenuItem)
            .FirstOrDefault(item => item is { IsFolder: false, Invoke: not null });
    }

    private static MenuFlyoutItemBase? FirstFocusableItem(IEnumerable<MenuFlyoutItemBase> items)
    {
        return items.FirstOrDefault(item => item is not MenuFlyoutSeparator && item.IsEnabled);
    }

    private static void FocusWhenLoaded(MenuFlyoutItemBase? item)
    {
        if (item is null)
        {
            return;
        }

        if (item.IsLoaded)
        {
            item.Focus(FocusState.Keyboard);
            return;
        }

        RoutedEventHandler? handler = null;
        handler = (_, _) =>
        {
            item.Loaded -= handler;
            item.Focus(FocusState.Keyboard);
        };
        item.Loaded += handler;
    }

    private void Invoke(QuickMenuItem item, bool asPlainText)
    {
        var action = asPlainText ? item.PlainTextInvoke : item.Invoke;
        if (action is null)
        {
            return;
        }

        BeginInvokeAndDismiss(action);
    }

    private void InvokePasteOption(QuickMenuPasteOption option)
    {
        BeginInvokeAndDismiss(option.Invoke);
    }

    private void InvokeEdit(QuickMenuItem item)
    {
        if (item.EditInvoke is null)
        {
            return;
        }

        BeginInvokeAndDismiss(item.EditInvoke);
    }

    private void InvokePreview(QuickMenuItem item)
    {
        if (item.PreviewInvoke is null)
        {
            return;
        }

        BeginInvokeAndDismiss(item.PreviewInvoke);
    }

    private void InvokeNumberShortcut(int index)
    {
        var item = _rootItems
            .Where(item => item is { IsEnabled: true, IsSeparator: false, IsFolder: false, IsNumberShortcutEnabled: true })
            .ElementAtOrDefault(index);
        if (item is not null)
        {
            Invoke(item, asPlainText: false);
        }
    }

    private void BeginInvokeAndDismiss(Action action)
    {
        _flyout.Hide();
        _appWindow?.Hide();
        _ = Task.Delay(90).ContinueWith(_ => DispatcherQueue.TryEnqueue(() =>
        {
            action();
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
        var placement = openAbove ? FlyoutPlacementMode.TopEdgeAlignedLeft : FlyoutPlacementMode.BottomEdgeAlignedLeft;

        return new RootFlyoutPlacement(anchor, placement);
    }

    internal static System.Drawing.Point GetCursorPoint()
    {
        return NativeMethods.GetCursorPos(out var point)
            ? new System.Drawing.Point(point.X, point.Y)
            : System.Drawing.Point.Empty;
    }

    internal static System.Drawing.Rectangle GetWorkingArea(System.Drawing.Point point)
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

    private static async Task InsertImagePreviewAsync(
        MenuFlyoutItemBase flyoutItem,
        byte[]? imageBytes,
        string size,
        QuickMenuThemePalette palette)
    {
        // Invoked from async-void Loaded handlers; an exception here would crash the app.
        try
        {
            await InsertImagePreviewCoreAsync(flyoutItem, imageBytes, size, palette);
        }
        catch (Exception exception)
        {
            AppDiagnostics.Log(exception, "Quick menu inline image preview");
        }
    }

    private static async Task InsertImagePreviewCoreAsync(
        MenuFlyoutItemBase flyoutItem,
        byte[]? imageBytes,
        string size,
        QuickMenuThemePalette palette)
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
            BorderBrush = palette.InlineImagePreviewBorder.ToBrush(),
            Background = palette.InlineImagePreviewBackground.ToBrush(),
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

    internal static async Task<BitmapImage> CreateBitmapImageAsync(byte[] bytes, int decodePixelWidth = 0)
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

    internal static void ConfigureBorderlessToolWindow(IntPtr hwnd)
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
    Action? EditInvoke = null,
    Action? PreviewInvoke = null,
    string? RevealedTitle = null,
    DateTimeOffset? CapturedAt = null,
    bool IsPinned = false,
    IReadOnlyCollection<ClipboardFormatKind>? Formats = null,
    bool IsNumberShortcutEnabled = false,
    Func<IReadOnlyList<QuickMenuPasteOption>>? LazyPasteOptions = null)
{
    private IReadOnlyList<QuickMenuItem>? _resolvedChildren = Children;
    private IReadOnlyList<QuickMenuPasteOption>? _resolvedPasteOptions = PasteOptions;

    public bool IsSelected { get; set; }

    public bool IsFolder => !IsSeparator && (_resolvedChildren is { Count: > 0 } || LazyChildren is not null);

    public bool HasPasteOptions => !IsSeparator && (_resolvedPasteOptions is { Count: > 0 } || LazyPasteOptions is not null);

    public Visibility FolderVisibility => IsFolder ? Visibility.Visible : Visibility.Collapsed;

    public string DisplayHint => IsFolder ? "\uE974" : CommandHint;

    public static QuickMenuItem Separator() => new(string.Empty, string.Empty, string.Empty, string.Empty, () => { }, IsEnabled: false, IsSeparator: true);

    public IReadOnlyList<QuickMenuItem> GetChildren()
    {
        if (_resolvedChildren is not null)
        {
            return _resolvedChildren;
        }

        var children = LazyChildren?.Invoke() ?? [];
        if (children.Count > 0)
        {
            _resolvedChildren = children;
        }

        return children;
    }

    public IReadOnlyList<QuickMenuPasteOption> GetPasteOptions()
    {
        if (_resolvedPasteOptions is not null)
        {
            return _resolvedPasteOptions;
        }

        _resolvedPasteOptions = LazyPasteOptions?.Invoke() ?? [];
        return _resolvedPasteOptions;
    }

    public override string ToString() => Title;
}

public sealed record QuickMenuPasteOption(
    string Text,
    string IconGlyph,
    Action Invoke,
    string? IconFontFamily = null,
    string? Id = null);

internal sealed record QuickMenuImagePreviewCommand(QuickMenuItem Item);

internal readonly record struct KeyModifierState(bool Control, bool Shift, bool Alt, bool Win)
{
    public static KeyModifierState None { get; } = new(false, false, false, false);
}

internal sealed record RootFlyoutPlacement(
    System.Drawing.Point Anchor,
    FlyoutPlacementMode Placement);
