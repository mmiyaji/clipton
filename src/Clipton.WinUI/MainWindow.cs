using Microsoft.UI.Xaml.Automation;
using Clipton.Core;
using Microsoft.UI;
using Microsoft.UI.Input;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI.Core;
using WinRT.Interop;

namespace Clipton.WinUI;

public sealed class MainWindow : Window
{
    private const int HistoryDisplayBatchSize = 50;
    private const double SettingsPageMaxWidth = 920;
    private const double SidebarExpandedWidth = 248;
    private const double SidebarCollapsedWidth = 72;
    private const double ContentMinWidth = 680;
    private const double SidebarAutoCollapseWidth = 1040;
    private const double SidebarAutoExpandWidth = 1120;
    private const double SettingControlHeight = 36;
    private const double SearchControlHeight = 32;
    private const string TermsUrl = "https://mmiyaji.github.io/clipton/terms/";
    private const string PrivacyUrl = "https://mmiyaji.github.io/clipton/privacy/";
    private const string AuthorUrl = "https://ruhenheim.org";
    private const string DonationUrl = "https://www.buymeacoffee.com/erumdoor";
    private const string DonationBannerUrl = "https://cdn.buymeacoffee.com/buttons/v2/default-yellow.png";
    private const string SnippetVariablesUrl = "https://mmiyaji.github.io/clipton/snippet-variables/";
    private readonly CliptonRuntime _runtime;
    private readonly Grid _root = new();
    private readonly SemaphoreSlim _dialogGate = new(1, 1);
    private readonly NavigationView _navigationView = new();
    private readonly ScrollViewer _contentScroller = new();
    private readonly StackPanel _generalPage = SettingsPage();
    private readonly StackPanel _historyPage = SettingsPage();
    private readonly StackPanel _historySettingsPage = SettingsPage();
    private readonly StackPanel _advancedPage = SettingsPage();
    private readonly StackPanel _snippetPage = SettingsPage();
    private readonly StackPanel _donationPage = SettingsPage();
    private readonly StackPanel _aboutPage = SettingsPage();
    private readonly StackPanel _historyListHost = new() { Spacing = 10 };
    private readonly ListView _historyListView = new();
    private readonly TextBlock _historyEmptyText = Description();
    private readonly TreeView _snippetTree = new();
    private readonly Dictionary<TreeViewNode, SnippetItemViewModel> _snippetNodes = [];
    private readonly Dictionary<TreeViewNode, string> _snippetFolderNodes = [];
    private readonly List<Border> _cards = [];
    private readonly List<NavigationViewItem> _navItems = [];
    private readonly Dictionary<ToggleSwitch, TextBlock> _toggleStateLabels = [];
    private readonly List<(TextBlock TextBlock, string Key)> _localizedTextBlocks = [];
    private readonly List<TextBlock> _descriptionTextBlocks = [];
    private readonly TextBlock _titleText = Header(20);
    private readonly TextBlock _hotkeyText = Description();
    private readonly UIElement _brandHeader;
    private readonly Image _appLogoImage = new() { Width = 32, Height = 32 };
    private readonly StackPanel _navigationPaneFooter = new() { Padding = new Thickness(8, 10, 8, 0), Spacing = 12 };
    private readonly Border _hotkeyPill;
    private readonly TextBlock _generalHeaderText = Header();
    private readonly TextBlock _generalDescriptionText = Description();
    private readonly TextBlock _historyHeaderText = Header();
    private readonly TextBlock _historyDescriptionText = Description();
    private readonly TextBlock _historySettingsHeaderText = Header();
    private readonly TextBlock _historySettingsDescriptionText = Description();
    private readonly TextBlock _advancedHeaderText = Header();
    private readonly TextBlock _advancedDescriptionText = Description();
    private readonly TextBlock _snippetHeaderText = Header();
    private readonly TextBlock _snippetDescriptionText = Description();
    private readonly TextBlock _snippetFormTitle = Header(18);
    private readonly TextBlock _donationHeaderText = Header();
    private readonly TextBlock _donationDescriptionText = Description();
    private readonly TextBlock _aboutHeaderText = Header();
    private readonly TextBlock _aboutDescriptionText = Description();
    private readonly ComboBox _hotkeyBox = new();
    private readonly Button _captureHotkeyButton = new();
    private readonly Button _resetHotkeyButton = new();
    private readonly ComboBox _themeBox = new();
    private readonly ComboBox _localeBox = new();
    private readonly ComboBox _quickMenuTopLevelHistoryItemsBox = new();
    private readonly ComboBox _quickMenuDisplayModeBox = new();
    private readonly ComboBox _quickMenuImagePreviewSizeBox = new();
    private readonly ComboBox _quickMenuSearchShortcutBox = new();
    private readonly ComboBox _quickMenuPlainTextShortcutBox = new();
    private readonly ComboBox _quickMenuMaskShortcutBox = new();
    private readonly ComboBox _quickMenuCapturedAtShortcutBox = new();
    private readonly Dictionary<string, (CheckBox CheckBox, string LabelKey)> _quickMenuPasteOptionChecks = [];
    private readonly ToggleSwitch _quickMenuShowCapturedAtToggle = CompactToggle();
    private readonly ToggleSwitch _quickMenuShowShortcutHintsToggle = CompactToggle();
    private readonly ToggleSwitch _startupToggle = CompactToggle();
    private readonly ToggleSwitch _hideSettingsWindowOnStartupToggle = CompactToggle();
    private readonly ToggleSwitch _pauseCaptureToggle = CompactToggle();
    private readonly ToggleSwitch _diagnosticLoggingToggle = CompactToggle();
    private readonly ToggleSwitch _persistHistoryToggle = CompactToggle();
    private readonly ToggleSwitch _historyAccessLockToggle = CompactToggle();
    private readonly ComboBox _historyAccessLockTimeoutBox = new();
    private readonly Button _historyAccessLockPinButton = new();
    private readonly Button _historyAccessLockResetButton = new();
    private readonly Button _historyAccessLockNowButton = new();
    private readonly TextBlock _historyAccessLockStatusText = Description();
    private readonly ToggleSwitch _maskSensitiveContentToggle = CompactToggle();
    private readonly Button _maskDefinitionsButton = new();
    private readonly StackPanel _maskDefinitionsPanel = new() { Spacing = 12 };
    private readonly ComboBox _maskPrefixBox = new();
    private readonly ToggleSwitch _maskEmailToggle = CompactToggle();
    private readonly ToggleSwitch _maskCreditCardToggle = CompactToggle();
    private readonly ToggleSwitch _maskSecretKeywordToggle = CompactToggle();
    private readonly ToggleSwitch _maskBearerTokenToggle = CompactToggle();
    private readonly ToggleSwitch _maskLongTokenToggle = CompactToggle();
    private readonly ToggleSwitch _maskShortAlphanumericCodeToggle = CompactToggle();
    private readonly ToggleSwitch _maskPhoneNumberToggle = CompactToggle();
    private readonly ToggleSwitch _maskCustomPatternToggle = CompactToggle();
    private readonly Dictionary<string, TextBox> _maskRulePatternBoxes = [];
    private readonly TextBox _maskPatternsBox = new();
    private readonly TextBox _maskTestBox = new();
    private readonly Button _maskDefinitionsResetButton = new();
    private readonly Button _maskDefinitionsSaveButton = new();
    private readonly TextBlock _maskDefinitionsErrorText = Description();
    private readonly TextBlock _maskTestResultText = Description();
    private readonly ComboBox _maxHistoryItemsBox = new();
    private readonly ComboBox _clipboardCaptureDelayBox = new();
    private readonly Button _registerFromHistoryButton = new();
    private readonly Button _exportHistoryButton = new();
    private readonly Button _importHistoryButton = new();
    private readonly Button _clearButton = new();
    private readonly Grid _historySearchHost = new();
    private readonly TextBox _historySearchBox = new();
    private readonly FontIcon _historySearchIcon = new();
    private readonly Button _advancedHistorySearchButton = new();
    private readonly Button _clearHistorySearchButton = new();
    private readonly StackPanel _historyAdvancedSearchPanel = new() { Spacing = 10 };
    private readonly ToggleButton _historyTypeTextFilter = new();
    private readonly ToggleButton _historyTypeRichFilter = new();
    private readonly ToggleButton _historyTypeHtmlFilter = new();
    private readonly ToggleButton _historyTypeImageFilter = new();
    private readonly ToggleButton _historyTypeFileFilter = new();
    private readonly ToggleButton _historyPinnedFilter = new();
    private readonly ToggleButton _historyUrlFilter = new();
    private readonly Button _loadMoreHistoryButton = new();
    private readonly TextBlock _historySearchStatusText = Description();
    private readonly TextBlock _selectedSnippetText = Description();
    private readonly Button _newSnippetButton = new();
    private readonly Button _exportSnippetsButton = new();
    private readonly Button _importSnippetsButton = new();
    private readonly Button _saveSnippetButton = new();
    private readonly Button _pasteSnippetButton = new();
    private readonly Button _deleteSnippetButton = new();
    private readonly Button _termsButton = new();
    private readonly Button _privacyButton = new();
    private readonly Button _donationButton = new();
    private readonly TextBlock _donationFallbackText = new();
    private readonly HyperlinkButton _donationLinkButton = new();
    private readonly Button _exitApplicationButton = new();
    private readonly Button _openLogsButton = new();
    private readonly Button _clearLogsButton = new();
    private readonly TextBlock _dataDirectoryText = Description();
    private readonly Button _openDataDirectoryButton = new();
    private readonly Button _changeDataDirectoryButton = new();
    private readonly Button _resetDataDirectoryButton = new();
    private string? _selectedHistoryId;
    private SnippetItemViewModel? _selectedSnippet;
    private string _selectedSnippetFolder = string.Empty;
    private bool _updatingSnippetTreeSelection;
    private string _historySearchQuery = string.Empty;
    private int _historyVisibleLimit = HistoryDisplayBatchSize;
    private readonly Dictionary<string, ListViewItem> _historyRowCache = new(StringComparer.Ordinal);
    private bool _loading;
    private bool _startupRegistrationSaving;
    private bool _historyOptionsSaving;
    private bool _snippetDeleteInProgress;
    private bool _historyItemDeleteInProgress;
    private bool _updatingNavSelection;
    private bool _updatingHistorySearchBox;
    private bool _updatingHistorySearchFilters;
    private int _selectedPageIndex;
    private bool _sidebarCollapsed;
    private bool _autoSidebarApplied;
    private Microsoft.UI.Windowing.AppWindow? _appWindow;
    private readonly NativeMethods.WindowProc _windowProc;
    private IntPtr _hwnd;
    private IntPtr _originalWndProc;
    private bool _hiddenToTray;
    private bool _maskDefinitionsExpanded;
    private bool _onboardingDialogOpen;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _onboardingTimer;
    private Border? _maskDefinitionsCard;

    public MainWindow(CliptonRuntime runtime)
    {
        _runtime = runtime;
        _brandHeader = BuildBrandHeader();
        _hotkeyPill = Pill(_hotkeyText);
        _windowProc = WindowProc;
        TrackDescriptionText(
            _hotkeyText,
            _generalDescriptionText,
            _historyDescriptionText,
            _historySettingsDescriptionText,
            _advancedDescriptionText,
            _snippetDescriptionText,
            _donationDescriptionText,
            _aboutDescriptionText,
            _historySearchStatusText,
            _historyEmptyText,
            _selectedSnippetText,
            _dataDirectoryText,
            _historyAccessLockStatusText,
            _maskDefinitionsErrorText,
            _maskTestResultText);
        Title = "Clipton";
        BuildUi();
        ApplyTheme();
        SizeWindow();
        RefreshTexts();
        RefreshItems();
        Closed += (_, _) => _runtime.OnMainWindowClosed(this);
    }

    public void ShowSettingsWindow()
    {
        if (_hiddenToTray)
        {
            _appWindow?.Show();
            _hiddenToTray = false;
        }

        Activate();
    }

    public bool IsHiddenToTray => _hiddenToTray;

    public void HideSettingsWindowToTray()
    {
        _appWindow?.Hide();
        _hiddenToTray = true;
    }

    public async Task ShowHistoryPageAsync()
    {
        if (!await EnsureHistoryAccessUnlockedAsync(showWindow: true))
        {
            return;
        }

        SelectPage(1);
        ShowSettingsWindow();
    }

    public async Task<bool> RequestHistoryAccessUnlockAsync(bool showWindow)
    {
        return await EnsureHistoryAccessUnlockedAsync(showWindow);
    }

    public void HideHistoryPageForLock()
    {
        if (IsProtectedPageIndex(_selectedPageIndex))
        {
            SelectPage(0);
        }
    }

    private async Task<bool> EnsureHistoryAccessUnlockedAsync(bool showWindow)
    {
        if (!_runtime.IsHistoryAccessLockEnabled)
        {
            return true;
        }

        if (!_runtime.RequiresHistoryAccessUnlock)
        {
            _runtime.RefreshHistoryAccessUnlockWindow();
            return true;
        }

        if (IsProtectedPageIndex(_selectedPageIndex))
        {
            SelectPage(0);
        }

        if (showWindow)
        {
            ShowSettingsWindow();
        }

        return await PromptForHistoryAccessUnlockAsync();
    }

    public void ShowOnboardingIfNeeded()
    {
        if (_runtime.Settings.InitialLaunchCompleted || _onboardingDialogOpen)
        {
            return;
        }

        _onboardingDialogOpen = true;
        _onboardingTimer?.Stop();
        var timer = DispatcherQueue.CreateTimer();
        _onboardingTimer = timer;
        timer.Interval = TimeSpan.FromMilliseconds(250);
        timer.IsRepeating = false;
        timer.Tick += async (_, _) =>
        {
            timer.Stop();
            _onboardingTimer = null;
            try
            {
                await ShowOnboardingDialogAsync();
            }
            catch (Exception exception)
            {
                AppDiagnostics.Log(exception, "Onboarding");
            }
            finally
            {
                _onboardingDialogOpen = false;
            }
        };
        timer.Start();
    }

    public void RefreshItems()
    {
        // Display-affecting state (masking, language, pin state, ...) may have
        // changed, so cached rows must be rebuilt.
        _historyRowCache.Clear();
        RefreshItemsCore();
    }

    // Membership-only refresh (new capture, search filter, load more): row
    // visuals per snapshot are unchanged, so cached rows are reused instead of
    // rebuilding view models, row UI and context flyouts for every item.
    public void RefreshItemsIncremental()
    {
        RefreshItemsCore();
    }

    private void RefreshItemsCore()
    {
        var filter = SearchFilter.Parse(_historySearchQuery);
        _historyListView.Items.Clear();
        var visibleHistoryItems = new List<ClipboardSnapshot>(_historyVisibleLimit);
        var matchedHistoryCount = 0;
        foreach (var snapshot in _runtime.History.Items)
        {
            if (!HistoryMatchesSearch(filter, snapshot))
            {
                continue;
            }

            matchedHistoryCount++;
            if (visibleHistoryItems.Count < _historyVisibleLimit)
            {
                visibleHistoryItems.Add(snapshot);
            }
        }

        PruneHistoryRowCache();
        foreach (var snapshot in visibleHistoryItems)
        {
            _historyListView.Items.Add(GetOrCreateHistoryRow(snapshot));
        }

        _historyEmptyText.Text = string.IsNullOrWhiteSpace(_historySearchQuery) ? _runtime.Translate("HistoryEmpty") : _runtime.Translate("NoSearchResults");
        _historyEmptyText.Visibility = visibleHistoryItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var hasMoreLoadedMatches = matchedHistoryCount > visibleHistoryItems.Count;
        var hasUnloadedHistory = _runtime.UnloadedPersistedHistoryCount > 0;
        _loadMoreHistoryButton.Visibility = hasMoreLoadedMatches || hasUnloadedHistory ? Visibility.Visible : Visibility.Collapsed;
        if (hasMoreLoadedMatches || hasUnloadedHistory)
        {
            var remaining = string.IsNullOrWhiteSpace(_historySearchQuery)
                ? Math.Max(0, _runtime.AvailableHistoryCount - visibleHistoryItems.Count)
                : Math.Max(0, matchedHistoryCount - visibleHistoryItems.Count) + _runtime.UnloadedPersistedHistoryCount;
            _loadMoreHistoryButton.Content = string.Format(_runtime.Translate("LoadMoreHistory"), remaining);
        }

        if (visibleHistoryItems.Count == 0)
        {
            _selectedHistoryId = null;
        }

        if (_selectedHistoryId is not null)
        {
            foreach (ListViewItem item in _historyListView.Items)
            {
                if (item.Tag is HistoryItemViewModel historyItem && string.Equals(historyItem.Id, _selectedHistoryId, StringComparison.Ordinal))
                {
                    _historyListView.SelectedItem = item;
                    break;
                }
            }
        }

        RefreshSnippetTree();
    }

    private ListViewItem GetOrCreateHistoryRow(ClipboardSnapshot snapshot)
    {
        if (_historyRowCache.TryGetValue(snapshot.Id, out var cached))
        {
            return cached;
        }

        var item = _runtime.CreateHistoryItemViewModel(snapshot);
        var listItem = new ListViewItem
        {
            Content = HistoryListRow(item),
            Tag = item,
            ContextFlyout = CreateHistoryContextFlyout(item),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(0),
            MinHeight = 56,
            UseSystemFocusVisuals = true
        };
        listItem.DoubleTapped += (_, _) => _runtime.PasteHistoryItem(item.Id, asPlainText: false);
        listItem.RightTapped += (_, _) =>
        {
            _selectedHistoryId = item.Id;
            _historyListView.SelectedItem = listItem;
        };
        _historyRowCache[snapshot.Id] = listItem;
        return listItem;
    }

    private void PruneHistoryRowCache()
    {
        if (_historyRowCache.Count == 0)
        {
            return;
        }

        var activeIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var snapshot in _runtime.History.Items)
        {
            activeIds.Add(snapshot.Id);
        }

        foreach (var staleId in _historyRowCache.Keys.Where(id => !activeIds.Contains(id)).ToArray())
        {
            _historyRowCache.Remove(staleId);
        }
    }

    public void RefreshTexts()
    {
        _loading = true;
        var t = _runtime.Translate;
        _titleText.Text = t("AppName");
        _hotkeyText.Text = $"{t("Hotkey")}: {_runtime.Settings.Hotkey}";
        UpdateNavButtonContents();
        _generalHeaderText.Text = t("General");
        _generalDescriptionText.Text = t("GeneralDescription");
        _historyHeaderText.Text = t("History");
        _historyDescriptionText.Text = t("HistoryDescription");
        _historySettingsHeaderText.Text = t("HistorySettings");
        _historySettingsDescriptionText.Text = t("HistorySettingsDescription");
        _advancedHeaderText.Text = t("Advanced");
        _advancedDescriptionText.Text = t("AdvancedDescription");
        _snippetHeaderText.Text = t("Snippets");
        _snippetDescriptionText.Text = t("SnippetDescription");
        _snippetFormTitle.Text = t("SnippetEditor");
        _donationHeaderText.Text = t("Donation");
        _donationDescriptionText.Text = t("DonationDescription");
        _aboutHeaderText.Text = t("About");
        _aboutDescriptionText.Text = t("AboutDescription");
        SetCommandButton(_registerFromHistoryButton, "\uE8EC", t("RegisterFromHistory"));
        SetCommandButton(_exportHistoryButton, "\uEDE1", t("Export"));
        SetCommandButton(_importHistoryButton, "\uE896", t("Import"));
        SetCommandButton(_clearButton, "\uE74D", t("ClearHistory"));
        SetCommandButton(_maskDefinitionsButton, "\uE713", t("MaskDefinitions"));
        _historySearchBox.PlaceholderText = t("SearchPlaceholder");
        SetIconButton(_advancedHistorySearchButton, "\uE71C", t("AdvancedSearch"));
        SetCommandButton(_clearHistorySearchButton, "\uE711", t("ClearSearch"));
        SetHistoryFilterToggle(_historyTypeTextFilter, t("FormatText"));
        SetHistoryFilterToggle(_historyTypeRichFilter, t("FormatRichText"));
        SetHistoryFilterToggle(_historyTypeHtmlFilter, t("FormatHtml"));
        SetHistoryFilterToggle(_historyTypeImageFilter, t("Image"));
        SetHistoryFilterToggle(_historyTypeFileFilter, t("FormatFileDrop"));
        SetHistoryFilterToggle(_historyPinnedFilter, t("PinnedHistory"));
        SetHistoryFilterToggle(_historyUrlFilter, t("SearchFilterUrl"));
        _loadMoreHistoryButton.Content = string.Format(t("LoadMoreHistory"), 0);
        SetCommandButton(_newSnippetButton, "\uE710", t("NewSnippet"));
        SetCommandButton(_exportSnippetsButton, "\uEDE1", t("Export"));
        SetCommandButton(_importSnippetsButton, "\uE896", t("Import"));
        SetCommandButton(_saveSnippetButton, "\uE70F", t("EditSnippet"));
        SetCommandButton(_pasteSnippetButton, "\uE77F", t("Paste"));
        SetCommandButton(_deleteSnippetButton, "\uE74D", t("Delete"));
        _termsButton.Content = t("TermsOfUse");
        _privacyButton.Content = t("PrivacyPolicy");
        _donationFallbackText.Text = t("BuyMeACoffee");
        _donationLinkButton.Content = t("DonationOpenBuyMeACoffee");
        AutomationProperties.SetName(_donationButton, t("BuyMeACoffee"));
        AutomationProperties.SetName(_donationLinkButton, t("DonationOpenBuyMeACoffee"));
        SetCommandButton(_exitApplicationButton, "\uE8BB", t("ExitApplication"));
        SetCommandButton(_openLogsButton, "\uE838", t("OpenLogs"));
        SetCommandButton(_clearLogsButton, "\uE74D", t("ClearLogs"));
        SetCommandButton(_openDataDirectoryButton, "\uE838", t("OpenFolder"));
        SetCommandButton(_changeDataDirectoryButton, "\uE8B7", t("Change"));
        SetCommandButton(_resetDataDirectoryButton, "\uE777", t("UseDefault"));
        UpdateDataDirectoryText();
        _maskPatternsBox.PlaceholderText = t("MaskPatternDefinitions");
        _maskTestBox.PlaceholderText = t("MaskTestText");
        _maskDefinitionsResetButton.Content = t("ResetToDefaults");
        AutomationProperties.SetName(_maskDefinitionsResetButton, t("ResetToDefaults"));
        _maskDefinitionsSaveButton.Content = t("Save");
        AutomationProperties.SetName(_maskDefinitionsSaveButton, t("Save"));
        _captureHotkeyButton.Content = t("CaptureHotkey");
        _resetHotkeyButton.Content = t("ResetHotkey");
        SetComboBoxText(_themeBox, "system", t("ThemeSystem"));
        SetComboBoxText(_themeBox, "light", t("ThemeLight"));
        SetComboBoxText(_themeBox, "dark", t("ThemeDark"));
        SetComboBoxText(_localeBox, "system", t("LanguageSystem"));
        SetComboBoxText(_localeBox, "en", t("LanguageEnglish"));
        SetComboBoxText(_localeBox, "ja", t("LanguageJapanese"));
        SetComboBoxText(_quickMenuDisplayModeBox, "default", t("QuickMenuDisplayModeDefault"));
        SetComboBoxText(_quickMenuDisplayModeBox, "rich", t("QuickMenuDisplayModeRich"));
        SetComboBoxText(_quickMenuImagePreviewSizeBox, "none", t("ImagePreviewSizeNone"));
        SetComboBoxText(_quickMenuImagePreviewSizeBox, "small", t("ImagePreviewSizeSmall"));
        SetComboBoxText(_quickMenuImagePreviewSizeBox, "medium", t("ImagePreviewSizeMedium"));
        SetComboBoxText(_quickMenuImagePreviewSizeBox, "large", t("ImagePreviewSizeLarge"));
        foreach (var count in QuickMenuHistoryBuckets.TopLevelHistoryItemOptions)
        {
            SetComboBoxText(_quickMenuTopLevelHistoryItemsBox, count.ToString(), count.ToString());
        }
        SetComboBoxText(_clipboardCaptureDelayBox, "0", t("ClipboardCaptureDelayImmediate"));
        SetComboBoxText(_clipboardCaptureDelayBox, "50", string.Format(t("Milliseconds"), 50));
        SetComboBoxText(_clipboardCaptureDelayBox, "100", string.Format(t("Milliseconds"), 100));
        SetComboBoxText(_clipboardCaptureDelayBox, "150", string.Format(t("Milliseconds"), 150));
        SetComboBoxText(_clipboardCaptureDelayBox, "250", string.Format(t("Milliseconds"), 250));
        SetComboBoxText(_clipboardCaptureDelayBox, "500", string.Format(t("Milliseconds"), 500));
        SetComboBoxText(_clipboardCaptureDelayBox, "1000", string.Format(t("Milliseconds"), 1000));
        SetComboBoxText(_historyAccessLockTimeoutBox, "0", t("HistoryAccessLockTimeoutEveryTime"));
        SetComboBoxText(_historyAccessLockTimeoutBox, "1", string.Format(t("Minutes"), 1));
        SetComboBoxText(_historyAccessLockTimeoutBox, "5", string.Format(t("Minutes"), 5));
        SetComboBoxText(_historyAccessLockTimeoutBox, "15", string.Format(t("Minutes"), 15));
        SetComboBoxText(_historyAccessLockTimeoutBox, "30", string.Format(t("Minutes"), 30));
        SetComboBoxText(_historyAccessLockTimeoutBox, "60", string.Format(t("Minutes"), 60));
        SetCommandButton(_historyAccessLockPinButton, "\uE72E", _runtime.IsHistoryAccessLockConfigured ? t("ChangePin") : t("SetPin"));
        SetCommandButton(_historyAccessLockResetButton, "\uE777", t("ResetPin"));
        SetCommandButton(_historyAccessLockNowButton, "\uE785", t("LockNow"));
        _startupToggle.IsOn = _runtime.Settings.StartWithWindows;
        _hideSettingsWindowOnStartupToggle.IsOn = _runtime.Settings.HideSettingsWindowOnStartup;
        _pauseCaptureToggle.IsOn = _runtime.Settings.PauseCapture;
        _diagnosticLoggingToggle.IsOn = _runtime.Settings.DiagnosticLoggingEnabled;
        _persistHistoryToggle.IsOn = _runtime.Settings.PersistEncryptedHistory;
        _historyAccessLockToggle.IsOn = _runtime.Settings.HistoryAccessLockEnabled && _runtime.IsHistoryAccessLockConfigured;
        _maskSensitiveContentToggle.IsOn = _runtime.Settings.MaskSensitiveContent;
        ApplyMaskRuleDefinitionsToUi();
        _maskCustomPatternToggle.IsOn = _runtime.Settings.MaskRules.CustomPattern;
        EnsureHistoryLimitComboItem(_runtime.Settings.MaxHistoryItems);
        SetComboSelection(_maxHistoryItemsBox, _runtime.Settings.MaxHistoryItems.ToString(), refreshSelectionBox: true);
        SetComboSelection(_clipboardCaptureDelayBox, _runtime.Settings.ClipboardCaptureDelayMilliseconds.ToString(), refreshSelectionBox: true);
        SetComboSelection(_historyAccessLockTimeoutBox, _runtime.Settings.HistoryAccessLockTimeoutMinutes.ToString(), refreshSelectionBox: true);
        SetComboSelection(_quickMenuTopLevelHistoryItemsBox, _runtime.Settings.QuickMenuTopLevelHistoryItems.ToString(), refreshSelectionBox: true);
        _quickMenuShowCapturedAtToggle.IsOn = _runtime.Settings.QuickMenuShowCapturedAt;
        _quickMenuShowShortcutHintsToggle.IsOn = _runtime.Settings.QuickMenuShowShortcutHints;
        _startupToggle.IsEnabled = !_runtime.IsSafeMode;
        RefreshToggleStateLabels();
        RefreshLocalizedTextBlocks();
        EnsureHotkeyComboItem(_runtime.Settings.Hotkey);
        SetComboSelection(_hotkeyBox, _runtime.Settings.Hotkey, refreshSelectionBox: true);
        SetComboSelection(_themeBox, _runtime.Settings.Theme, refreshSelectionBox: true);
        SetComboSelection(_localeBox, _runtime.Settings.Locale, refreshSelectionBox: true);
        SetComboSelection(_quickMenuDisplayModeBox, _runtime.Settings.QuickMenuDisplayMode, refreshSelectionBox: true);
        SetComboSelection(_quickMenuImagePreviewSizeBox, _runtime.Settings.QuickMenuImagePreviewSize, refreshSelectionBox: true);
        SetComboSelection(_quickMenuSearchShortcutBox, _runtime.Settings.QuickMenuShortcuts.Search, refreshSelectionBox: true);
        SetComboSelection(_quickMenuPlainTextShortcutBox, _runtime.Settings.QuickMenuShortcuts.PastePlainText, refreshSelectionBox: true);
        SetComboSelection(_quickMenuMaskShortcutBox, _runtime.Settings.QuickMenuShortcuts.ToggleMaskReveal, refreshSelectionBox: true);
        SetComboSelection(_quickMenuCapturedAtShortcutBox, _runtime.Settings.QuickMenuShortcuts.ToggleCapturedAt, refreshSelectionBox: true);
        RefreshQuickMenuPasteOptionChecks();
        UpdateHistoryAccessLockUi();
        UpdateSelectedSnippetText();
        UpdateHistorySearchStatus();
        UpdateMaskTestPreview();
        _loading = false;
    }

    private void BuildUi()
    {
        _root.SizeChanged += (_, e) => UpdateSidebarForWindowWidth(e.NewSize.Width);
        Content = _root;

        _navigationView.IsSettingsVisible = false;
        _navigationView.IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed;
        _navigationView.PaneDisplayMode = NavigationViewPaneDisplayMode.Left;
        _navigationView.OpenPaneLength = SidebarExpandedWidth;
        _navigationView.CompactPaneLength = SidebarCollapsedWidth;
        _navigationView.IsPaneOpen = true;
        _navigationView.PaneTitle = string.Empty;
        _navigationView.SelectionChanged += async (_, args) =>
        {
            if (_updatingNavSelection || args.SelectedItem is not NavigationViewItem item || item.Tag is not int index)
            {
                return;
            }

            await SelectPageAsync(index);
        };
        _navigationView.PaneOpened += (_, _) =>
        {
            _sidebarCollapsed = false;
            UpdateNavigationPaneFooterVisibility();
        };
        _navigationView.PaneClosed += (_, _) =>
        {
            _sidebarCollapsed = true;
            UpdateNavigationPaneFooterVisibility();
        };
        _navigationPaneFooter.Children.Add(_hotkeyPill);
        _hotkeyText.Margin = new Thickness(8, 0, 8, 4);
        _navigationPaneFooter.Children.Add(_brandHeader);
        _navigationView.PaneFooter = _navigationPaneFooter;
        foreach (var index in Enumerable.Range(0, 7))
        {
            var item = CreateNavItem(index);
            _navItems.Add(item);
            _navigationView.MenuItems.Add(item);
        }

        _contentScroller.MinWidth = ContentMinWidth;
        _contentScroller.Padding = new Thickness(36, 30, 36, 16);
        _contentScroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        _contentScroller.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        var contentHost = new Grid { HorizontalAlignment = HorizontalAlignment.Left, MinWidth = ContentMinWidth };
        _contentScroller.Content = contentHost;
        _contentScroller.SizeChanged += (_, e) =>
        {
            UpdateSettingsPageWidth(Math.Max(0, e.NewSize.Width - _contentScroller.Padding.Left - _contentScroller.Padding.Right));
        };
        contentHost.Children.Add(_generalPage);
        contentHost.Children.Add(_historyPage);
        contentHost.Children.Add(_historySettingsPage);
        contentHost.Children.Add(_advancedPage);
        contentHost.Children.Add(_snippetPage);
        contentHost.Children.Add(_donationPage);
        contentHost.Children.Add(_aboutPage);
        _navigationView.Content = _contentScroller;
        _root.Children.Add(_navigationView);

        BuildGeneralPage();
        BuildHistoryPage();
        BuildHistorySettingsPage();
        BuildAdvancedPage();
        BuildSnippetPage();
        BuildDonationPage();
        BuildAboutPage();
        SelectPage(0);
    }

    private void BuildGeneralPage()
    {
        _generalPage.Children.Add(PageHeader(_generalHeaderText, _generalDescriptionText));
        _generalPage.Children.Add(SectionHeader("ActivationSection"));
        foreach (var hotkey in HotkeyGesture.Presets.Select(preset => preset.ToString()))
        {
            _hotkeyBox.Items.Add(new ComboBoxItem { Content = hotkey, Tag = hotkey });
        }

        _hotkeyBox.SelectionChanged += async (_, _) =>
        {
            if (_loading || (_hotkeyBox.SelectedItem as ComboBoxItem)?.Tag is not string hotkey) return;
            await TryApplyHotkeyAsync(hotkey);
        };
        _captureHotkeyButton.Click += async (_, _) => await CaptureCustomHotkeyAsync();
        _resetHotkeyButton.Click += async (_, _) => await ConfirmAndResetHotkeyAsync();
        var hotkeyControls = new Grid
        {
            ColumnSpacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        hotkeyControls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        hotkeyControls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        hotkeyControls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _hotkeyBox.MinWidth = 180;
        AlignSettingControl(_hotkeyBox);
        AlignSettingControl(_captureHotkeyButton);
        AlignSettingControl(_resetHotkeyButton);
        hotkeyControls.Children.Add(_hotkeyBox);
        Grid.SetColumn(_captureHotkeyButton, 1);
        hotkeyControls.Children.Add(_captureHotkeyButton);
        Grid.SetColumn(_resetHotkeyButton, 2);
        hotkeyControls.Children.Add(_resetHotkeyButton);
        _generalPage.Children.Add(SettingCard("\uE765", "Hotkey", "HotkeyDescription", hotkeyControls));

        _themeBox.Items.Add(new ComboBoxItem { Tag = "system" });
        _themeBox.Items.Add(new ComboBoxItem { Tag = "light" });
        _themeBox.Items.Add(new ComboBoxItem { Tag = "dark" });
        _themeBox.SelectionChanged += (_, _) =>
        {
            if (_loading || (_themeBox.SelectedItem as ComboBoxItem)?.Tag is not string theme) return;
            _runtime.SetTheme(theme);
            ApplyTheme();
        };
        _generalPage.Children.Add(SettingCard("\uE790", "Theme", "ThemeDescription", _themeBox));

        _localeBox.Items.Add(new ComboBoxItem { Tag = "system" });
        _localeBox.Items.Add(new ComboBoxItem { Tag = "en" });
        _localeBox.Items.Add(new ComboBoxItem { Tag = "ja" });
        _localeBox.SelectionChanged += (_, _) =>
        {
            if (_loading || (_localeBox.SelectedItem as ComboBoxItem)?.Tag is not string locale) return;
            _runtime.SetLocale(locale);
            RefreshTexts();
        };
        _generalPage.Children.Add(SettingCard("\uE8C1", "Language", "LanguageDescription", _localeBox));

        _startupToggle.Toggled += (_, _) => _ = SetStartupAsyncSafely();
        _generalPage.Children.Add(SettingCard("\uE7C3", "Startup", "StartupDescription", _startupToggle));
        _hideSettingsWindowOnStartupToggle.Toggled += (_, _) => SaveStartupWindowOptions();
        _generalPage.Children.Add(SettingCard("\uE8BB", "HideSettingsWindowOnStartup", "HideSettingsWindowOnStartupDescription", _hideSettingsWindowOnStartupToggle));

        _generalPage.Children.Add(SectionHeader("StorageSection"));
        _generalPage.Children.Add(BuildDataDirectoryCard());

        _generalPage.Children.Add(SectionHeader("QuickMenuSection"));
        foreach (var count in QuickMenuHistoryBuckets.TopLevelHistoryItemOptions)
        {
            _quickMenuTopLevelHistoryItemsBox.Items.Add(new ComboBoxItem { Tag = count.ToString() });
        }

        _quickMenuTopLevelHistoryItemsBox.Width = 180;
        _quickMenuTopLevelHistoryItemsBox.SelectionChanged += (_, _) => ChangeQuickMenuTopLevelHistoryItems();
        _generalPage.Children.Add(SettingCard("\uE8B7", "QuickMenuTopLevelHistoryItems", "QuickMenuTopLevelHistoryItemsDescription", _quickMenuTopLevelHistoryItemsBox));

        foreach (var mode in new[] { "default", "rich" })
        {
            _quickMenuDisplayModeBox.Items.Add(new ComboBoxItem { Tag = mode });
        }

        _quickMenuDisplayModeBox.Width = 180;
        _quickMenuDisplayModeBox.SelectionChanged += (_, _) => ChangeQuickMenuDisplayMode();
        _generalPage.Children.Add(SettingCard("\uE771", "QuickMenuDisplayMode", "QuickMenuDisplayModeDescription", _quickMenuDisplayModeBox));

        foreach (var size in new[] { "none", "small", "medium", "large" })
        {
            _quickMenuImagePreviewSizeBox.Items.Add(new ComboBoxItem { Tag = size });
        }

        _quickMenuImagePreviewSizeBox.Width = 180;
        _quickMenuImagePreviewSizeBox.SelectionChanged += (_, _) => ChangeQuickMenuImagePreviewSize();
        _generalPage.Children.Add(SettingCard("\uEB9F", "ImagePreviewSize", "ImagePreviewSizeDescription", _quickMenuImagePreviewSizeBox));
        _quickMenuShowCapturedAtToggle.Toggled += (_, _) => SaveQuickMenuDisplayOptions();
        _generalPage.Children.Add(SettingCard("\uE823", "QuickMenuShowCapturedAt", "QuickMenuShowCapturedAtDescription", _quickMenuShowCapturedAtToggle));
        _quickMenuShowShortcutHintsToggle.Toggled += (_, _) => SaveQuickMenuDisplayOptions();
        _generalPage.Children.Add(SettingCard("\uE765", "QuickMenuShowShortcutHints", "QuickMenuShowShortcutHintsDescription", _quickMenuShowShortcutHintsToggle));

        _generalPage.Children.Add(SectionHeader("QuickMenuShortcutsSection"));
        _generalPage.Children.Add(BuildQuickMenuShortcutsPanel());
    }

    private void BuildAdvancedPage()
    {
        _advancedPage.Children.Add(PageHeader(_advancedHeaderText, _advancedDescriptionText));
        _advancedPage.Children.Add(SectionHeader("QuickMenuPasteOptionsSection"));
        _advancedPage.Children.Add(BuildQuickMenuPasteOptionsPanel());
    }

    private UIElement BuildQuickMenuPasteOptionsPanel()
    {
        var panel = new StackPanel { Spacing = 14 };
        panel.Children.Add(DescriptionText("QuickMenuPasteOptionsDescription", fontSize: 12, wrapping: TextWrapping.Wrap));
        panel.Children.Add(BuildQuickMenuPasteOptionGroup(
            "QuickMenuPasteOptionsCommonGroup",
            [
                (QuickMenuPasteOptionIds.PasteOriginal, "PasteOriginal"),
                (QuickMenuPasteOptionIds.TogglePin, "QuickMenuPasteOptionTogglePin")
            ]));
        panel.Children.Add(BuildQuickMenuPasteOptionGroup(
            "QuickMenuPasteOptionsTextGroup",
            [
                (QuickMenuPasteOptionIds.PastePlain, "PastePlain"),
                (QuickMenuPasteOptionIds.EditAndPaste, "EditAndPaste"),
                (QuickMenuPasteOptionIds.PasteNoLineBreaks, "PasteNoLineBreaks"),
                (QuickMenuPasteOptionIds.PasteUppercase, "PasteUppercase"),
                (QuickMenuPasteOptionIds.PasteLowercase, "PasteLowercase"),
                (QuickMenuPasteOptionIds.PasteTrimmed, "PasteTrimmed"),
                (QuickMenuPasteOptionIds.PasteJsonString, "PasteJsonString"),
                (QuickMenuPasteOptionIds.PasteExtractUrls, "PasteExtractUrls"),
                (QuickMenuPasteOptionIds.PasteFormattedJson, "PasteFormattedJson")
            ]));
        panel.Children.Add(BuildQuickMenuPasteOptionGroup(
            "QuickMenuPasteOptionsImageGroup",
            [
                (QuickMenuPasteOptionIds.PasteImageOriginal, "PasteImageOriginal"),
                (QuickMenuPasteOptionIds.PasteImagePng, "PasteImagePng"),
                (QuickMenuPasteOptionIds.PasteImageJpeg, "PasteImageJpeg"),
                (QuickMenuPasteOptionIds.PasteImageFile, "PasteImageFile"),
                (QuickMenuPasteOptionIds.CopyImageOnly, "CopyImageOnly")
            ]));
        panel.Children.Add(BuildQuickMenuPasteOptionGroup(
            "QuickMenuPasteOptionsFileGroup",
            [
                (QuickMenuPasteOptionIds.PasteFilePaths, "PasteFilePaths"),
                (QuickMenuPasteOptionIds.PasteFileNames, "PasteFileNames"),
                (QuickMenuPasteOptionIds.PasteFileNamesWithoutExtension, "PasteFileNamesWithoutExtension"),
                (QuickMenuPasteOptionIds.PasteFileDirectories, "PasteFileDirectories")
            ]));
        return Card(panel);
    }

    private UIElement BuildQuickMenuPasteOptionGroup(string titleKey, IReadOnlyList<(string Id, string LabelKey)> options)
    {
        var group = new StackPanel { Spacing = 0 };
        var header = DescriptionText(
            titleKey,
            fontSize: 13,
            fontWeight: Microsoft.UI.Text.FontWeights.SemiBold,
            wrapping: TextWrapping.NoWrap);
        header.Margin = new Thickness(0, 0, 0, 4);
        group.Children.Add(header);

        for (var i = 0; i < options.Count; i++)
        {
            group.Children.Add(QuickMenuPasteOptionRow(options[i].Id, options[i].LabelKey));
            if (i < options.Count - 1)
            {
                group.Children.Add(RowSeparator());
            }
        }

        return group;
    }

    private UIElement QuickMenuPasteOptionRow(string optionId, string labelKey)
    {
        var row = new Grid
        {
            Padding = new Thickness(0, 7, 0, 7),
            ColumnSpacing = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(LocalizedText(labelKey, wrapping: TextWrapping.Wrap));

        var checkBox = new CheckBox
        {
            MinWidth = 0,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        checkBox.Checked += (_, _) => SaveQuickMenuPasteOption(checkBox, optionId);
        checkBox.Unchecked += (_, _) => SaveQuickMenuPasteOption(checkBox, optionId);
        _quickMenuPasteOptionChecks[optionId] = (checkBox, labelKey);
        Grid.SetColumn(checkBox, 1);
        row.Children.Add(checkBox);
        return row;
    }

    private UIElement BuildQuickMenuShortcutsPanel()
    {
        ConfigureShortcutCombo(
            _quickMenuSearchShortcutBox,
            nameof(QuickMenuShortcutSettings.Search),
            ["Ctrl+S", "Ctrl+F", "S", "F"]);
        ConfigureShortcutCombo(
            _quickMenuPlainTextShortcutBox,
            nameof(QuickMenuShortcutSettings.PastePlainText),
            ["T", "P", "Ctrl+T", "Ctrl+P"]);
        ConfigureShortcutCombo(
            _quickMenuMaskShortcutBox,
            nameof(QuickMenuShortcutSettings.ToggleMaskReveal),
            ["Ctrl+M"]);
        ConfigureShortcutCombo(
            _quickMenuCapturedAtShortcutBox,
            nameof(QuickMenuShortcutSettings.ToggleCapturedAt),
            ["Ctrl+D", "D"]);

        var rows = new StackPanel { Spacing = 0 };
        rows.Children.Add(ShortcutSettingRow("QuickMenuShortcutSearch", "QuickMenuShortcutSearchDescription", _quickMenuSearchShortcutBox));
        rows.Children.Add(RowSeparator());
        rows.Children.Add(ShortcutSettingRow("QuickMenuShortcutPastePlainText", "QuickMenuShortcutPastePlainTextDescription", _quickMenuPlainTextShortcutBox));
        rows.Children.Add(RowSeparator());
        rows.Children.Add(ShortcutSettingRow("QuickMenuShortcutToggleMask", "QuickMenuShortcutToggleMaskDescription", _quickMenuMaskShortcutBox));
        rows.Children.Add(RowSeparator());
        rows.Children.Add(ShortcutSettingRow("QuickMenuShortcutToggleCapturedAt", "QuickMenuShortcutToggleCapturedAtDescription", _quickMenuCapturedAtShortcutBox));
        rows.Children.Add(RowSeparator());
        rows.Children.Add(ShortcutReadOnlyRow("QuickMenuShortcutImagePreview", "QuickMenuShortcutImagePreviewDescription", "Space"));
        rows.Children.Add(RowSeparator());
        rows.Children.Add(ShortcutReadOnlyRow("QuickMenuShortcutImagePreviewCopy", "QuickMenuShortcutImagePreviewCopyDescription", "Ctrl+C / Ctrl+X"));
        rows.Children.Add(RowSeparator());
        rows.Children.Add(ShortcutReadOnlyRow("QuickMenuShortcutImagePreviewZoom", "QuickMenuShortcutImagePreviewZoomDescription", "Ctrl++ / Ctrl+- / Ctrl+0"));
        rows.Children.Add(RowSeparator());
        rows.Children.Add(ShortcutReadOnlyRow("QuickMenuShortcutNavigate", "QuickMenuShortcutNavigateDescription", "\u2191 / \u2193 / \u2190 / \u2192"));
        rows.Children.Add(RowSeparator());
        rows.Children.Add(ShortcutReadOnlyRow("QuickMenuShortcutConfirm", "QuickMenuShortcutConfirmDescription", "Enter"));
        rows.Children.Add(RowSeparator());
        rows.Children.Add(ShortcutReadOnlyRow("QuickMenuShortcutCancel", "QuickMenuShortcutCancelDescription", "Esc"));
        return Card(rows, new Thickness(0));
    }

    private void ConfigureShortcutCombo(ComboBox comboBox, string action, IEnumerable<string> shortcuts)
    {
        comboBox.Items.Clear();
        foreach (var shortcut in shortcuts)
        {
            comboBox.Items.Add(new ComboBoxItem { Content = shortcut, Tag = shortcut });
        }

        comboBox.Width = 150;
        AlignSettingControl(comboBox);
        comboBox.SelectionChanged += (_, _) => ChangeQuickMenuShortcut(action, comboBox);
    }

    private UIElement ShortcutSettingRow(string titleKey, string descriptionKey, FrameworkElement control)
    {
        control.HorizontalAlignment = HorizontalAlignment.Right;
        var row = ShortcutRow(titleKey, descriptionKey);
        Grid.SetColumn(control, 1);
        row.Children.Add(control);
        return row;
    }

    private UIElement ShortcutReadOnlyRow(string titleKey, string descriptionKey, string shortcut)
    {
        var row = ShortcutRow(titleKey, descriptionKey);
        var keyText = new TextBlock
        {
            Text = shortcut,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextAlignment = TextAlignment.Right,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 230
        };
        Grid.SetColumn(keyText, 1);
        row.Children.Add(keyText);
        return row;
    }

    private Grid ShortcutRow(string titleKey, string descriptionKey)
    {
        var row = new Grid
        {
            Padding = new Thickness(16, 11, 16, 11),
            ColumnSpacing = 16,
            VerticalAlignment = VerticalAlignment.Center
        };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var texts = new StackPanel { Spacing = 3 };
        texts.Children.Add(LocalizedText(titleKey, fontWeight: Microsoft.UI.Text.FontWeights.SemiBold));
        texts.Children.Add(DescriptionText(descriptionKey, fontSize: 12, wrapping: TextWrapping.Wrap));
        row.Children.Add(texts);
        return row;
    }

    private Border RowSeparator() => new()
    {
        Height = 1,
        Margin = new Thickness(16, 0, 16, 0),
        Background = CardBorderBrush()
    };

    private void BuildHistoryPage()
    {
        _historyPage.Children.Add(PageHeader(_historyHeaderText, _historyDescriptionText));
        _historyPage.Children.Add(SectionHeader("HistorySection"));
        _historyPage.Children.Add(BuildHistorySearchPanel());
        _historyListView.SelectionMode = ListViewSelectionMode.Single;
        _historyListView.HorizontalAlignment = HorizontalAlignment.Stretch;
        _historyListView.SelectionChanged += (_, _) =>
        {
            if (_historyListView.SelectedItem is ListViewItem { Tag: HistoryItemViewModel item })
            {
                _selectedHistoryId = item.Id;
            }
        };
        _historyListView.DoubleTapped += (_, _) =>
        {
            if (_historyListView.SelectedItem is ListViewItem { Tag: HistoryItemViewModel item })
            {
                _runtime.PasteHistoryItem(item.Id, asPlainText: false);
            }
        };
        _historyEmptyText.Margin = new Thickness(4, 6, 4, 6);
        _loadMoreHistoryButton.HorizontalAlignment = HorizontalAlignment.Left;
        _historyListHost.Children.Add(_historyListView);
        _historyListHost.Children.Add(_historyEmptyText);
        _historyListHost.Children.Add(_loadMoreHistoryButton);

        var actions = new Grid { ColumnSpacing = 12 };
        actions.ColumnDefinitions.Add(new ColumnDefinition());
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var primaryActions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, Spacing = 8 };
        var dataActions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        _registerFromHistoryButton.Click += (_, _) => RegisterSelectedHistory();
        _clearButton.Click += async (_, _) => await ConfirmAndClearHistoryAsync();
        _loadMoreHistoryButton.Click += (_, _) => LoadMoreHistory();
        _exportHistoryButton.Click += async (_, _) => await ExportHistoryAsync();
        _importHistoryButton.Click += async (_, _) => await ImportHistoryAsync();
        primaryActions.Children.Add(_registerFromHistoryButton);
        dataActions.Children.Add(_exportHistoryButton);
        dataActions.Children.Add(_importHistoryButton);
        dataActions.Children.Add(_clearButton);
        actions.Children.Add(primaryActions);
        Grid.SetColumn(dataActions, 1);
        actions.Children.Add(dataActions);
        _historyPage.Children.Add(actions);
        _historyPage.Children.Add(_historySearchStatusText);
        _historyPage.Children.Add(Card(_historyListHost, new Thickness(8, 8, 8, 10)));
    }

    private void BuildHistorySettingsPage()
    {
        _historySettingsPage.Children.Add(PageHeader(_historySettingsHeaderText, _historySettingsDescriptionText));
        _historySettingsPage.Children.Add(SectionHeader("CapturePrivacySection"));
        foreach (var toggle in new[] { _pauseCaptureToggle, _persistHistoryToggle, _maskSensitiveContentToggle })
        {
            toggle.Toggled += async (_, _) => await SaveHistoryOptionsAsync();
        }

        _historySettingsPage.Children.Add(SettingCard("\uE769", "PauseCapture", "PauseCaptureDescription", _pauseCaptureToggle));
        _diagnosticLoggingToggle.Toggled += (_, _) => SaveDiagnosticLogging();
        var diagnosticControls = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        _openLogsButton.Click += (_, _) => _runtime.OpenDiagnosticLogDirectory();
        _clearLogsButton.Click += async (_, _) => await ConfirmAndClearLogsAsync();
        diagnosticControls.Children.Add(_openLogsButton);
        diagnosticControls.Children.Add(_clearLogsButton);
        diagnosticControls.Children.Add(ToggleActionHost(_diagnosticLoggingToggle));
        _historySettingsPage.Children.Add(SettingCard("\uE946", "DiagnosticLogging", "DiagnosticLoggingDescription", diagnosticControls));
        _historySettingsPage.Children.Add(SettingCard("\uE72E", "PersistHistory", "PersistHistoryDescription", _persistHistoryToggle));
        _historySettingsPage.Children.Add(BuildHistoryAccessLockCard());
        _maxHistoryItemsBox.Width = 180;
        foreach (var count in new[] { 50, 100, 200, 500, 1000 })
        {
            _maxHistoryItemsBox.Items.Add(new ComboBoxItem { Tag = count.ToString(), Content = count.ToString() });
        }

        _maxHistoryItemsBox.SelectionChanged += async (_, _) => await ChangeMaxHistoryItemsAsync();
        _historySettingsPage.Children.Add(SettingCard("\uE81C", "MaxHistoryItems", "MaxHistoryItemsDescription", _maxHistoryItemsBox));
        _clipboardCaptureDelayBox.Width = 180;
        foreach (var delay in new[] { 0, 50, 100, 150, 250, 500, 1000 })
        {
            _clipboardCaptureDelayBox.Items.Add(new ComboBoxItem { Tag = delay.ToString() });
        }

        _clipboardCaptureDelayBox.SelectionChanged += (_, _) => ChangeClipboardCaptureDelay();
        _historySettingsPage.Children.Add(SettingCard("\uE916", "ClipboardCaptureDelay", "ClipboardCaptureDelayDescription", _clipboardCaptureDelayBox));
        var maskControls = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        maskControls.Children.Add(_maskDefinitionsButton);
        maskControls.Children.Add(ToggleActionHost(_maskSensitiveContentToggle));
        _maskDefinitionsButton.Click += (_, _) => ToggleMaskDefinitionsPanel();
        _historySettingsPage.Children.Add(SettingCard("\uE8D7", "MaskSensitiveContent", "MaskSensitiveContentDescription", maskControls));
        _historySettingsPage.Children.Add(BuildMaskDefinitionsPanel());
    }

    private UIElement BuildHistoryAccessLockCard()
    {
        _historyAccessLockTimeoutBox.Width = 180;
        foreach (var minutes in HistoryAccessLockCredential.AllowedTimeoutMinutes)
        {
            _historyAccessLockTimeoutBox.Items.Add(new ComboBoxItem { Tag = minutes.ToString() });
        }

        _historyAccessLockToggle.Toggled += async (_, _) => await ChangeHistoryAccessLockEnabledAsync();
        _historyAccessLockTimeoutBox.SelectionChanged += (_, _) => ChangeHistoryAccessLockTimeout();
        _historyAccessLockPinButton.Click += async (_, _) => await ChangeHistoryAccessPinAsync();
        _historyAccessLockResetButton.Click += async (_, _) => await ConfirmAndResetHistoryAccessLockAsync();
        _historyAccessLockNowButton.Click += (_, _) =>
        {
            _runtime.LockHistoryAccess();
            UpdateHistoryAccessLockUi();
        };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        buttons.Children.Add(_historyAccessLockPinButton);
        buttons.Children.Add(_historyAccessLockResetButton);
        buttons.Children.Add(_historyAccessLockNowButton);
        buttons.Children.Add(ToggleActionHost(_historyAccessLockToggle));

        var controls = new StackPanel
        {
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        _historyAccessLockStatusText.TextAlignment = TextAlignment.Right;
        _historyAccessLockStatusText.TextWrapping = TextWrapping.Wrap;
        _historyAccessLockStatusText.MaxWidth = 420;
        controls.Children.Add(_historyAccessLockStatusText);
        controls.Children.Add(_historyAccessLockTimeoutBox);
        controls.Children.Add(buttons);
        return SettingCard("\uE72E", "HistoryAccessLock", "HistoryAccessLockDescription", controls);
    }

    private UIElement BuildDataDirectoryCard()
    {
        var dataDirectoryControls = new StackPanel
        {
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        _dataDirectoryText.TextWrapping = TextWrapping.Wrap;
        _dataDirectoryText.TextAlignment = TextAlignment.Right;
        _dataDirectoryText.MaxWidth = 420;
        var dataDirectoryButtons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        _openDataDirectoryButton.Click += (_, _) => _runtime.OpenDataDirectory();
        _changeDataDirectoryButton.Click += async (_, _) => await ChangeDataDirectoryAsync();
        _resetDataDirectoryButton.Click += async (_, _) => await ResetDataDirectoryAsync();
        dataDirectoryButtons.Children.Add(_openDataDirectoryButton);
        dataDirectoryButtons.Children.Add(_changeDataDirectoryButton);
        dataDirectoryButtons.Children.Add(_resetDataDirectoryButton);
        dataDirectoryControls.Children.Add(_dataDirectoryText);
        dataDirectoryControls.Children.Add(dataDirectoryButtons);
        return SettingCard("\uE8B7", "DataDirectory", "DataDirectoryDescription", dataDirectoryControls);
    }

    private UIElement BuildHistorySearchPanel()
    {
        _historySearchHost.Height = SearchControlHeight;
        _historySearchHost.HorizontalAlignment = HorizontalAlignment.Stretch;
        _historySearchBox.Height = SearchControlHeight;
        _historySearchBox.MinHeight = SearchControlHeight;
        _historySearchBox.PlaceholderText = _runtime.Translate("SearchPlaceholder");
        _historySearchBox.Text = _historySearchQuery;
        _historySearchBox.Padding = new Thickness(34, 4, 34, 0);
        _historySearchBox.VerticalContentAlignment = VerticalAlignment.Center;
        _historySearchBox.VerticalAlignment = VerticalAlignment.Stretch;
        _historySearchBox.HorizontalAlignment = HorizontalAlignment.Stretch;
        _historySearchBox.TextChanged += (_, _) =>
        {
            if (_updatingHistorySearchBox)
            {
                return;
            }

            ApplyHistorySearch(BuildHistorySearchQueryFromFilters(_historySearchBox.Text));
        };
        _historySearchIcon.Glyph = "\uE721";
        _historySearchIcon.FontFamily = new FontFamily("Segoe Fluent Icons");
        _historySearchIcon.FontSize = 14;
        _historySearchIcon.Foreground = DescriptionBrush();
        _historySearchIcon.IsHitTestVisible = false;
        _historySearchIcon.HorizontalAlignment = HorizontalAlignment.Left;
        _historySearchIcon.VerticalAlignment = VerticalAlignment.Center;
        _historySearchIcon.Width = 16;
        _historySearchIcon.Margin = new Thickness(12, 0, 0, 0);
        _historySearchHost.Children.Add(_historySearchBox);
        _historySearchHost.Children.Add(_historySearchIcon);

        _advancedHistorySearchButton.Width = SearchControlHeight;
        _advancedHistorySearchButton.MinWidth = SearchControlHeight;
        _advancedHistorySearchButton.Height = SearchControlHeight;
        _advancedHistorySearchButton.MinHeight = SearchControlHeight;
        _advancedHistorySearchButton.Padding = new Thickness(0);
        _advancedHistorySearchButton.Opacity = 0.72;
        _advancedHistorySearchButton.Click += (_, _) => ToggleAdvancedHistorySearch();
        _clearHistorySearchButton.MinHeight = SearchControlHeight;
        _clearHistorySearchButton.Height = SearchControlHeight;
        _clearHistorySearchButton.Click += (_, _) => ClearHistorySearch();

        BuildHistoryAdvancedSearchPanel();

        var row = new Grid { ColumnSpacing = 12, MinHeight = SearchControlHeight };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(_historySearchHost);
        Grid.SetColumn(_advancedHistorySearchButton, 1);
        row.Children.Add(_advancedHistorySearchButton);
        Grid.SetColumn(_clearHistorySearchButton, 2);
        row.Children.Add(_clearHistorySearchButton);

        return new StackPanel
        {
            Spacing = 8,
            Children =
            {
                row,
                _historyAdvancedSearchPanel
            }
        };
    }

    private void BuildHistoryAdvancedSearchPanel()
    {
        _historyAdvancedSearchPanel.Visibility = Visibility.Collapsed;
        _historyAdvancedSearchPanel.Margin = new Thickness(2, 4, 0, 0);

        var typeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        typeRow.Children.Add(DescriptionText("SearchFilterFormat", fontSize: 12));
        typeRow.Children.Add(_historyTypeTextFilter);
        typeRow.Children.Add(_historyTypeRichFilter);
        typeRow.Children.Add(_historyTypeHtmlFilter);
        typeRow.Children.Add(_historyTypeImageFilter);
        typeRow.Children.Add(_historyTypeFileFilter);

        var optionRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        optionRow.Children.Add(DescriptionText("SearchFilterOptions", fontSize: 12));
        optionRow.Children.Add(_historyPinnedFilter);
        optionRow.Children.Add(_historyUrlFilter);

        foreach (var button in HistoryFilterButtons())
        {
            button.Height = 30;
            button.MinWidth = 72;
            button.Padding = new Thickness(12, 0, 12, 0);
            button.VerticalAlignment = VerticalAlignment.Center;
            button.HorizontalContentAlignment = HorizontalAlignment.Center;
            button.VerticalContentAlignment = VerticalAlignment.Center;
            button.Checked += (_, _) => ApplyAdvancedHistoryFiltersFromUi();
            button.Unchecked += (_, _) => ApplyAdvancedHistoryFiltersFromUi();
        }

        _historyAdvancedSearchPanel.Children.Add(typeRow);
        _historyAdvancedSearchPanel.Children.Add(optionRow);
    }

    private UIElement BuildMaskDefinitionsPanel()
    {
        _maskPrefixBox.Width = 150;
        _maskPrefixBox.Height = SettingControlHeight;
        _maskPrefixBox.VerticalAlignment = VerticalAlignment.Center;
        _maskPrefixBox.Items.Clear();
        for (var i = 0; i <= 12; i++)
        {
            _maskPrefixBox.Items.Add(new ComboBoxItem { Content = i.ToString(), Tag = i.ToString() });
        }
        SetComboSelection(_maskPrefixBox, Math.Clamp(_runtime.Settings.MaskVisiblePrefixLength, 0, 12).ToString());
        _maskPrefixBox.SelectionChanged += (_, _) => UpdateMaskTestPreview();

        var prefixRow = new Grid { ColumnSpacing = 16 };
        prefixRow.ColumnDefinitions.Add(new ColumnDefinition());
        prefixRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var prefixTexts = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        prefixTexts.Children.Add(LocalizedText("MaskVisiblePrefixLength", fontWeight: Microsoft.UI.Text.FontWeights.SemiBold));
        prefixTexts.Children.Add(DescriptionText("MaskSensitiveContentDescription", fontSize: 12, wrapping: TextWrapping.Wrap));
        prefixRow.Children.Add(prefixTexts);
        Grid.SetColumn(_maskPrefixBox, 1);
        prefixRow.Children.Add(_maskPrefixBox);

        foreach (var toggle in new[]
        {
            _maskEmailToggle,
            _maskCreditCardToggle,
            _maskSecretKeywordToggle,
            _maskBearerTokenToggle,
            _maskLongTokenToggle,
            _maskShortAlphanumericCodeToggle,
            _maskPhoneNumberToggle,
            _maskCustomPatternToggle
        })
        {
            toggle.Toggled += (_, _) => UpdateMaskTestPreview();
        }

        _maskPatternsBox.MinHeight = 120;
        _maskPatternsBox.HorizontalAlignment = HorizontalAlignment.Stretch;
        _maskPatternsBox.AcceptsReturn = true;
        _maskPatternsBox.TextWrapping = TextWrapping.NoWrap;
        _maskPatternsBox.Text = string.Join(Environment.NewLine, _runtime.Settings.CustomMaskPatterns);
        _maskPatternsBox.PlaceholderText = _runtime.Translate("MaskPatternDefinitions");
        _maskPatternsBox.Padding = new Thickness(12, 8, 12, 8);
        _maskPatternsBox.TextChanged += (_, _) => UpdateMaskTestPreview();

        _maskTestBox.MinHeight = 64;
        _maskTestBox.HorizontalAlignment = HorizontalAlignment.Stretch;
        _maskTestBox.AcceptsReturn = true;
        _maskTestBox.TextWrapping = TextWrapping.Wrap;
        _maskTestBox.PlaceholderText = _runtime.Translate("MaskTestText");
        _maskTestBox.Padding = new Thickness(12, 8, 12, 8);
        _maskTestBox.TextChanged += (_, _) => UpdateMaskTestPreview();
        _maskTestResultText.Text = _runtime.Translate("MaskTestEmpty");
        _maskTestResultText.Margin = new Thickness(2, 0, 0, 0);

        _maskDefinitionsErrorText.Visibility = Visibility.Collapsed;
        _maskDefinitionsResetButton.MinWidth = 128;
        _maskDefinitionsResetButton.Height = 32;
        _maskDefinitionsResetButton.HorizontalAlignment = HorizontalAlignment.Right;
        _maskDefinitionsResetButton.Margin = new Thickness(0, 2, 0, 0);
        _maskDefinitionsResetButton.Click += (_, _) => ResetMaskDefinitionsToDefaults();
        _maskDefinitionsSaveButton.MinWidth = 96;
        _maskDefinitionsSaveButton.Height = 32;
        _maskDefinitionsSaveButton.HorizontalAlignment = HorizontalAlignment.Right;
        _maskDefinitionsSaveButton.Margin = new Thickness(0, 2, 0, 0);
        _maskDefinitionsSaveButton.Click += (_, _) => SaveMaskDefinitions();
        var actionRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };
        actionRow.Children.Add(_maskDefinitionsResetButton);
        actionRow.Children.Add(_maskDefinitionsSaveButton);

        _maskDefinitionsPanel.Children.Add(prefixRow);
        _maskDefinitionsPanel.Children.Add(LocalizedText("MaskRuleOptions", fontWeight: Microsoft.UI.Text.FontWeights.SemiBold));
        _maskDefinitionsPanel.Children.Add(DescriptionText("MaskRuleOptionsDescription", fontSize: 12, wrapping: TextWrapping.Wrap));
        _maskDefinitionsPanel.Children.Add(BuildMaskRuleOptions());
        _maskDefinitionsPanel.Children.Add(LocalizedText("MaskPatternDefinitions", fontWeight: Microsoft.UI.Text.FontWeights.SemiBold));
        _maskDefinitionsPanel.Children.Add(DescriptionText("MaskDefinitionsDescription", fontSize: 12, wrapping: TextWrapping.Wrap));
        _maskDefinitionsPanel.Children.Add(_maskPatternsBox);
        _maskDefinitionsPanel.Children.Add(LocalizedText("MaskTestText", fontWeight: Microsoft.UI.Text.FontWeights.SemiBold));
        _maskDefinitionsPanel.Children.Add(DescriptionText("MaskTestDescription", fontSize: 12, wrapping: TextWrapping.Wrap));
        _maskDefinitionsPanel.Children.Add(_maskTestBox);
        _maskDefinitionsPanel.Children.Add(_maskTestResultText);
        _maskDefinitionsPanel.Children.Add(_maskDefinitionsErrorText);
        _maskDefinitionsPanel.Children.Add(actionRow);
        _maskDefinitionsCard = Card(_maskDefinitionsPanel);
        _maskDefinitionsCard.Visibility = Visibility.Collapsed;
        return _maskDefinitionsCard;
    }

    private FrameworkElement BuildMaskRuleOptions()
    {
        var grid = new Grid
        {
            ColumnSpacing = 12,
            RowSpacing = 8
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition());

        var rules = new (string Id, string Key, ToggleSwitch Toggle)[]
        {
            (MaskRuleIds.Email, "MaskRuleEmail", _maskEmailToggle),
            (MaskRuleIds.CreditCard, "MaskRuleCreditCard", _maskCreditCardToggle),
            (MaskRuleIds.SecretKeyword, "MaskRuleSecretKeyword", _maskSecretKeywordToggle),
            (MaskRuleIds.BearerToken, "MaskRuleBearerToken", _maskBearerTokenToggle),
            (MaskRuleIds.LongToken, "MaskRuleLongToken", _maskLongTokenToggle),
            (MaskRuleIds.ShortAlphanumericCode, "MaskRuleShortAlphanumericCode", _maskShortAlphanumericCodeToggle),
            (MaskRuleIds.PhoneNumber, "MaskRulePhoneNumber", _maskPhoneNumberToggle)
        };

        for (var i = 0; i < rules.Length; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var row = MaskRuleRow(rules[i].Id, rules[i].Key, rules[i].Toggle);
            Grid.SetRow(row, i);
            grid.Children.Add(row);
        }

        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var customPatternRow = MaskCustomPatternRow();
        Grid.SetRow(customPatternRow, rules.Length);
        grid.Children.Add(customPatternRow);

        return grid;
    }

    private FrameworkElement MaskRuleRow(string ruleId, string labelKey, ToggleSwitch toggle)
    {
        var row = new Grid { ColumnSpacing = 10, RowSpacing = 4 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = _runtime.Translate(labelKey);
        var labelBlock = LocalizedText(labelKey, fontSize: 12, fontWeight: Microsoft.UI.Text.FontWeights.SemiBold);
        labelBlock.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(labelBlock);

        var patternBox = new TextBox
        {
            Height = SettingControlHeight,
            Text = GetConfiguredMaskRule(ruleId).Pattern,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = 12,
            Padding = new Thickness(10, 6, 10, 6),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        patternBox.TextChanged += (_, _) => UpdateMaskTestPreview();
        AutomationProperties.SetName(patternBox, string.Format(_runtime.Translate("MaskRulePatternAutomationName"), label));
        _maskRulePatternBoxes[ruleId] = patternBox;
        Grid.SetColumn(patternBox, 1);
        row.Children.Add(patternBox);

        AutomationProperties.SetName(toggle, label);
        ToolTipService.SetToolTip(toggle, label);
        Grid.SetColumn(toggle, 2);
        row.Children.Add(toggle);
        return row;
    }

    private FrameworkElement MaskCustomPatternRow()
    {
        var row = new Grid { ColumnSpacing = 10, RowSpacing = 4 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = _runtime.Translate("MaskRuleCustomPattern");
        var labelBlock = LocalizedText("MaskRuleCustomPattern", fontSize: 12, fontWeight: Microsoft.UI.Text.FontWeights.SemiBold);
        labelBlock.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(labelBlock);

        var description = DescriptionText("MaskDefinitionsDescription", fontSize: 12, wrapping: TextWrapping.Wrap);
        Grid.SetColumn(description, 1);
        row.Children.Add(description);

        AutomationProperties.SetName(_maskCustomPatternToggle, label);
        ToolTipService.SetToolTip(_maskCustomPatternToggle, label);
        Grid.SetColumn(_maskCustomPatternToggle, 2);
        row.Children.Add(_maskCustomPatternToggle);
        return row;
    }

    private void ToggleMaskDefinitionsPanel()
    {
        _maskDefinitionsExpanded = !_maskDefinitionsExpanded;
        if (_maskDefinitionsCard is not null)
        {
            _maskDefinitionsCard.Visibility = _maskDefinitionsExpanded ? Visibility.Visible : Visibility.Collapsed;
        }
        if (_maskDefinitionsExpanded)
        {
            ScrollMaskDefinitionsIntoViewAndFocus();
        }
    }

    private async void ScrollMaskDefinitionsIntoViewAndFocus()
    {
        await Task.Delay(80);
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_maskDefinitionsExpanded || _maskDefinitionsCard is null)
            {
                return;
            }

            var point = _maskDefinitionsCard.TransformToVisual(_contentScroller).TransformPoint(new Windows.Foundation.Point(0, 0));
            var targetOffset = Math.Max(0, _contentScroller.VerticalOffset + point.Y - 28);
            _contentScroller.ChangeView(null, targetOffset, null, true);
        });

        await Task.Delay(180);
        DispatcherQueue.TryEnqueue(() =>
        {
            _maskEmailToggle.Focus(FocusState.Programmatic);
        });
    }

    private void SaveMaskDefinitions()
    {
        var patterns = GetCurrentMaskPatterns();
        var rules = GetCurrentMaskRules();
        var ruleDefinitions = GetCurrentMaskRuleDefinitions();
        var invalidRule = GetInvalidMaskRuleDefinition(ruleDefinitions);
        if (invalidRule is not null)
        {
            _maskDefinitionsErrorText.Text = string.Format(
                _runtime.Translate("MaskRuleDefinitionInvalid"),
                _runtime.Translate(invalidRule.NameKey),
                invalidRule.Pattern);
            _maskDefinitionsErrorText.Visibility = Visibility.Visible;
            return;
        }

        var invalidPatterns = rules.CustomPattern
            ? SensitiveContentDetector.GetInvalidCustomPatterns(patterns)
            : [];
        if (invalidPatterns.Length > 0)
        {
            _maskDefinitionsErrorText.Text = string.Format(_runtime.Translate("MaskDefinitionInvalid"), invalidPatterns[0]);
            _maskDefinitionsErrorText.Visibility = Visibility.Visible;
            return;
        }

        _maskDefinitionsErrorText.Visibility = Visibility.Collapsed;
        _runtime.SetMaskDefinitionOptions(GetSelectedMaskPrefixLength(), patterns, rules, ruleDefinitions);
        RefreshItems();
    }

    private void ResetMaskDefinitionsToDefaults()
    {
        SetComboSelection(_maskPrefixBox, "3");
        ApplyMaskRuleDefinitionsToUi(MaskRuleDefinitionDefaults.CreateDefaultRules());
        _maskCustomPatternToggle.IsOn = true;
        _maskPatternsBox.Text = string.Empty;
        _maskDefinitionsErrorText.Visibility = Visibility.Collapsed;
        UpdateMaskTestPreview();
    }

    private string[] GetCurrentMaskPatterns()
    {
        return _maskPatternsBox.Text
            .ReplaceLineEndings("\n")
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }

    private int GetSelectedMaskPrefixLength()
    {
        return _maskPrefixBox.SelectedItem is ComboBoxItem { Tag: string tag } && int.TryParse(tag, out var parsed)
            ? Math.Clamp(parsed, 0, 12)
            : 0;
    }

    private MaskRuleSettings GetCurrentMaskRules()
    {
        var rules = MaskRuleDefinitionDefaults.ToSettings(GetCurrentMaskRuleDefinitions());
        rules.CustomPattern = _maskCustomPatternToggle.IsOn;
        return rules;
    }

    private MaskRuleDefinition[] GetCurrentMaskRuleDefinitions()
    {
        return
        [
            CurrentRule(MaskRuleIds.Email, "MaskRuleEmail", 10, _maskEmailToggle),
            CurrentRule(MaskRuleIds.CreditCard, "MaskRuleCreditCard", 20, _maskCreditCardToggle),
            CurrentRule(MaskRuleIds.SecretKeyword, "MaskRuleSecretKeyword", 30, _maskSecretKeywordToggle),
            CurrentRule(MaskRuleIds.BearerToken, "MaskRuleBearerToken", 40, _maskBearerTokenToggle),
            CurrentRule(MaskRuleIds.LongToken, "MaskRuleLongToken", 50, _maskLongTokenToggle),
            CurrentRule(MaskRuleIds.ShortAlphanumericCode, "MaskRuleShortAlphanumericCode", 60, _maskShortAlphanumericCodeToggle),
            CurrentRule(MaskRuleIds.PhoneNumber, "MaskRulePhoneNumber", 70, _maskPhoneNumberToggle)
        ];
    }

    private MaskRuleDefinition CurrentRule(string id, string nameKey, int order, ToggleSwitch toggle)
    {
        var fallback = GetConfiguredMaskRule(id);
        return new MaskRuleDefinition
        {
            Id = id,
            NameKey = nameKey,
            Pattern = _maskRulePatternBoxes.TryGetValue(id, out var box) ? box.Text.Trim() : fallback.Pattern,
            Enabled = toggle.IsOn,
            BuiltIn = true,
            Order = order
        };
    }

    private MaskRuleDefinition GetConfiguredMaskRule(string id)
    {
        return MaskRuleDefinitionDefaults.Normalize(_runtime.Settings.MaskRuleDefinitions, _runtime.Settings.MaskRules)
            .First(rule => string.Equals(rule.Id, id, StringComparison.Ordinal));
    }

    private void ApplyMaskRuleDefinitionsToUi()
    {
        ApplyMaskRuleDefinitionsToUi(MaskRuleDefinitionDefaults.Normalize(_runtime.Settings.MaskRuleDefinitions, _runtime.Settings.MaskRules));
        _maskCustomPatternToggle.IsOn = _runtime.Settings.MaskRules.CustomPattern;
    }

    private void ApplyMaskRuleDefinitionsToUi(IEnumerable<MaskRuleDefinition> ruleDefinitions)
    {
        var rules = MaskRuleDefinitionDefaults.Normalize(ruleDefinitions)
            .ToDictionary(rule => rule.Id, StringComparer.Ordinal);
        _maskEmailToggle.IsOn = rules[MaskRuleIds.Email].Enabled;
        _maskCreditCardToggle.IsOn = rules[MaskRuleIds.CreditCard].Enabled;
        _maskSecretKeywordToggle.IsOn = rules[MaskRuleIds.SecretKeyword].Enabled;
        _maskBearerTokenToggle.IsOn = rules[MaskRuleIds.BearerToken].Enabled;
        _maskLongTokenToggle.IsOn = rules[MaskRuleIds.LongToken].Enabled;
        _maskShortAlphanumericCodeToggle.IsOn = rules[MaskRuleIds.ShortAlphanumericCode].Enabled;
        _maskPhoneNumberToggle.IsOn = rules[MaskRuleIds.PhoneNumber].Enabled;

        foreach (var (id, box) in _maskRulePatternBoxes)
        {
            box.Text = rules[id].Pattern;
        }
    }

    private static MaskRuleDefinition? GetInvalidMaskRuleDefinition(IEnumerable<MaskRuleDefinition> rules)
    {
        foreach (var rule in rules.Where(rule => rule.Enabled))
        {
            if (SensitiveContentDetector.GetInvalidCustomPatterns([rule.Pattern]).Length > 0)
            {
                return rule;
            }
        }

        return null;
    }

    private void UpdateMaskTestPreview()
    {
        if (_maskTestResultText is null)
        {
            return;
        }

        var testText = _maskTestBox.Text;
        if (string.IsNullOrWhiteSpace(testText))
        {
            _maskTestResultText.Text = _runtime.Translate("MaskTestEmpty");
            _maskTestResultText.Foreground = DescriptionBrush();
            return;
        }

        var rules = GetCurrentMaskRules();
        var ruleDefinitions = GetCurrentMaskRuleDefinitions();
        var invalidRule = GetInvalidMaskRuleDefinition(ruleDefinitions);
        if (invalidRule is not null)
        {
            _maskTestResultText.Text = string.Format(
                _runtime.Translate("MaskRuleDefinitionInvalid"),
                _runtime.Translate(invalidRule.NameKey),
                invalidRule.Pattern);
            _maskTestResultText.Foreground = Brush(IsDark ? "#FFB4AB" : "#B42318");
            return;
        }

        var patterns = GetCurrentMaskPatterns();
        var invalidPatterns = rules.CustomPattern
            ? SensitiveContentDetector.GetInvalidCustomPatterns(patterns)
            : [];
        if (invalidPatterns.Length > 0)
        {
            _maskTestResultText.Text = string.Format(_runtime.Translate("MaskDefinitionInvalid"), invalidPatterns[0]);
            _maskTestResultText.Foreground = Brush(IsDark ? "#FFB4AB" : "#B42318");
            return;
        }

        try
        {
            var preview = SensitiveContentDetector.CreateMaskedPreview(
                testText,
                GetSelectedMaskPrefixLength(),
                ruleDefinitions,
                patterns,
                rules.CustomPattern);
            var matchedRules = SensitiveContentDetector.FindMatchedRules(
                testText,
                ruleDefinitions,
                patterns,
                rules.CustomPattern);
            var result = preview is not null
                ? string.Format(
                    _runtime.Translate("MaskTestResult"),
                    string.Join(", ", matchedRules.Select(match => _runtime.Translate(match.NameKey)).Distinct(StringComparer.Ordinal)),
                    preview)
                : _runtime.Translate("MaskTestNoMatch");
            _maskTestResultText.Text = result;
            _maskTestResultText.Foreground = DescriptionBrush();
        }
        catch (RegexMatchTimeoutException)
        {
            _maskTestResultText.Text = _runtime.Translate("MaskTestTimeout");
            _maskTestResultText.Foreground = Brush(IsDark ? "#FFD86B" : "#9A6700");
        }
    }

    private bool HistoryMatchesSearch(SearchFilter filter, ClipboardSnapshot item)
    {
        if (filter.IsEmpty)
        {
            return true;
        }

        var plainText = ClipboardBridge.GetPlainText(item);
        if (!filter.MatchesDate(item.CapturedAt)
            || !filter.MatchesPinned(_runtime.IsHistoryPinned(item.Id))
            || !filter.MatchesType(item.Formats)
            || !filter.MatchesUrl(CliptonRuntime.ExtractUrls(plainText ?? string.Empty).Length > 0))
        {
            return false;
        }

        return filter.MatchesText(() =>
        {
            var formats = string.Join(" ", item.Formats);
            var snippet = _runtime.Snippets.FindByText(plainText);
            var preview = _runtime.CreateHistoryItemViewModel(item, includeThumbnail: false).Preview;
            return $"{formats} {snippet?.DisplayName} {preview} {plainText} {string.Join(" ", item.FilePaths)}";
        });
    }

    private void ApplyHistorySearch(string query)
    {
        var normalized = query.Trim();
        if (string.Equals(_historySearchQuery, normalized, StringComparison.Ordinal))
        {
            return;
        }

        _historySearchQuery = normalized;
        _historyVisibleLimit = HistoryDisplayBatchSize;
        UpdateHistorySearchStatus();
        RefreshItemsIncremental();
    }

    private void ClearHistorySearch()
    {
        ApplyHistorySearch(string.Empty);
    }

    private void ToggleAdvancedHistorySearch()
    {
        _historyAdvancedSearchPanel.Visibility = _historyAdvancedSearchPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void ApplyAdvancedHistoryFiltersFromUi()
    {
        if (_updatingHistorySearchFilters)
        {
            return;
        }

        _updatingHistorySearchFilters = true;
        try
        {
            var selectedTypeButton = CheckedHistoryTypeFilter();
            if (selectedTypeButton is not null)
            {
                foreach (var (button, _) in HistoryTypeFilterButtons())
                {
                    if (!ReferenceEquals(button, selectedTypeButton))
                    {
                        button.IsChecked = false;
                    }
                }
            }

            var query = BuildHistorySearchQueryFromFilters(_historySearchBox.Text);
            ApplyHistorySearch(query);
        }
        finally
        {
            _updatingHistorySearchFilters = false;
        }
    }

    private string BuildHistorySearchQueryFromFilters(string currentQuery)
    {
        var tokens = TokenizeSearchQuery(currentQuery)
            .Where(token => !IsUiManagedSearchFilterToken(token))
            .Select(QuoteSearchTokenIfNeeded)
            .ToList();

        var selectedType = SelectedHistoryTypeFilterValue();
        if (selectedType is not null)
        {
            tokens.Add($"type:{selectedType}");
        }

        if (_historyPinnedFilter.IsChecked == true)
        {
            tokens.Add("pinned:true");
        }

        if (_historyUrlFilter.IsChecked == true)
        {
            tokens.Add("url:true");
        }

        return string.Join(" ", tokens);
    }

    private void SyncAdvancedHistoryFiltersFromQuery()
    {
        if (_updatingHistorySearchFilters)
        {
            return;
        }

        var filter = SearchFilter.Parse(_historySearchQuery);
        _updatingHistorySearchFilters = true;
        try
        {
            foreach (var (button, value) in HistoryTypeFilterButtons())
            {
                button.IsChecked = string.Equals(filter.Type, value, StringComparison.OrdinalIgnoreCase);
            }

            _historyPinnedFilter.IsChecked = filter.Pinned == true;
            _historyUrlFilter.IsChecked = filter.HasUrl == true;
        }
        finally
        {
            _updatingHistorySearchFilters = false;
        }
    }

    private ToggleButton? CheckedHistoryTypeFilter()
    {
        return HistoryTypeFilterButtons().FirstOrDefault(item => item.Button.IsChecked == true).Button;
    }

    private string? SelectedHistoryTypeFilterValue()
    {
        return HistoryTypeFilterButtons().FirstOrDefault(item => item.Button.IsChecked == true).Value;
    }

    private IEnumerable<(ToggleButton Button, string Value)> HistoryTypeFilterButtons()
    {
        yield return (_historyTypeTextFilter, "text");
        yield return (_historyTypeRichFilter, "rich");
        yield return (_historyTypeHtmlFilter, "html");
        yield return (_historyTypeImageFilter, "image");
        yield return (_historyTypeFileFilter, "file");
    }

    private IEnumerable<ToggleButton> HistoryFilterButtons()
    {
        foreach (var (button, _) in HistoryTypeFilterButtons())
        {
            yield return button;
        }

        yield return _historyPinnedFilter;
        yield return _historyUrlFilter;
    }

    private static bool IsUiManagedSearchFilterToken(string token)
    {
        var separator = token.IndexOf(':');
        if (separator <= 0)
        {
            return false;
        }

        return token[..separator].ToLowerInvariant() switch
        {
            "type" or "format" or "pinned" or "pin" or "url" or "hasurl" => true,
            _ => false
        };
    }

    private static string QuoteSearchTokenIfNeeded(string token)
    {
        return token.Any(char.IsWhiteSpace)
            ? $"\"{token.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : token;
    }

    private static string[] TokenizeSearchQuery(string query)
    {
        var tokens = new List<string>();
        var current = new List<char>();
        var quoted = false;
        foreach (var ch in query)
        {
            if (ch == '"')
            {
                quoted = !quoted;
                continue;
            }

            if (char.IsWhiteSpace(ch) && !quoted)
            {
                AddCurrent();
                continue;
            }

            current.Add(ch);
        }

        AddCurrent();
        return tokens.ToArray();

        void AddCurrent()
        {
            if (current.Count == 0)
            {
                return;
            }

            var token = new string(current.ToArray()).Trim();
            if (token.Length > 0)
            {
                tokens.Add(token);
            }

            current.Clear();
        }
    }

    private void LoadMoreHistory()
    {
        _historyVisibleLimit += HistoryDisplayBatchSize;
        _runtime.LoadMorePersistedHistory(HistoryDisplayBatchSize);
        RefreshItemsIncremental();
    }

    private void UpdateHistorySearchStatus()
    {
        var hasQuery = !string.IsNullOrWhiteSpace(_historySearchQuery);
        _historySearchStatusText.Text = hasQuery
            ? string.Format(_runtime.Translate("SearchResults"), BuildHistorySearchStatusLabel())
            : string.Empty;
        _historySearchStatusText.Visibility = hasQuery ? Visibility.Visible : Visibility.Collapsed;
        _clearHistorySearchButton.Visibility = hasQuery ? Visibility.Visible : Visibility.Collapsed;
        var displayQuery = BuildHistorySearchTextBoxValue(_historySearchQuery);
        if (!string.Equals(_historySearchBox.Text, displayQuery, StringComparison.Ordinal))
        {
            _updatingHistorySearchBox = true;
            _historySearchBox.Text = displayQuery;
            _updatingHistorySearchBox = false;
        }

        SyncAdvancedHistoryFiltersFromQuery();
    }

    private string BuildHistorySearchTextBoxValue(string query)
    {
        return string.Join(" ", TokenizeSearchQuery(query)
            .Where(token => !IsUiManagedSearchFilterToken(token))
            .Select(QuoteSearchTokenIfNeeded));
    }

    private string BuildHistorySearchStatusLabel()
    {
        var parts = new List<string>();
        var keyword = BuildHistorySearchTextBoxValue(_historySearchQuery);
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            parts.Add(keyword);
        }

        var filter = SearchFilter.Parse(_historySearchQuery);
        var typeLabel = filter.Type?.ToLowerInvariant() switch
        {
            "text" => _runtime.Translate("FormatText"),
            "rich" or "rtf" => _runtime.Translate("FormatRichText"),
            "html" => _runtime.Translate("FormatHtml"),
            "image" => _runtime.Translate("Image"),
            "file" or "files" => _runtime.Translate("FormatFileDrop"),
            _ => null
        };
        if (typeLabel is not null)
        {
            parts.Add(typeLabel);
        }

        if (filter.Pinned == true)
        {
            parts.Add(_runtime.Translate("PinnedHistory"));
        }

        if (filter.HasUrl == true)
        {
            parts.Add(_runtime.Translate("SearchFilterUrl"));
        }

        return parts.Count == 0 ? _historySearchQuery : string.Join(" / ", parts);
    }

    private void BuildSnippetPage()
    {
        _snippetPage.Children.Add(PageHeader(_snippetHeaderText, _snippetDescriptionText));
        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        _snippetTree.SelectionMode = TreeViewSelectionMode.Single;
        _snippetTree.MinHeight = 180;
        _snippetTree.HorizontalAlignment = HorizontalAlignment.Stretch;
        _snippetTree.ItemTemplate = (DataTemplate)XamlReader.Load("""
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                <ContentPresenter Content="{Binding Content}" />
            </DataTemplate>
            """);
        _snippetTree.ItemInvoked += (_, e) =>
        {
            if (_updatingSnippetTreeSelection)
            {
                return;
            }

            if (TryGetSnippetFromTreeContent(e.InvokedItem, out var item))
            {
                SelectSnippet(item);
            }
        };
        _snippetTree.SelectionChanged += (_, _) =>
        {
            if (_updatingSnippetTreeSelection)
            {
                return;
            }

            if (_snippetTree.SelectedNode is { } node && _snippetNodes.TryGetValue(node, out var item))
            {
                SetSelectedSnippet(item);
            }
            else if (_snippetTree.SelectedNode is { } folderNode && _snippetFolderNodes.TryGetValue(folderNode, out var folder))
            {
                SetSelectedSnippetFolder(folder);
            }
        };
        grid.Children.Add(Card(_snippetTree));

        var details = new StackPanel { Spacing = 12 };
        var detailsHeader = new Grid { ColumnSpacing = 12 };
        detailsHeader.ColumnDefinitions.Add(new ColumnDefinition());
        detailsHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        detailsHeader.Children.Add(_snippetFormTitle);
        var snippetDataActions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        snippetDataActions.Children.Add(_exportSnippetsButton);
        snippetDataActions.Children.Add(_importSnippetsButton);
        Grid.SetColumn(snippetDataActions, 1);
        detailsHeader.Children.Add(snippetDataActions);
        details.Children.Add(detailsHeader);
        details.Children.Add(_selectedSnippetText);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        _newSnippetButton.Click += async (_, _) => await OpenSnippetEditorAsync(null, _selectedSnippetFolder, string.Empty, string.Empty);
        _saveSnippetButton.Click += async (_, _) =>
        {
            if (_selectedSnippet is null) return;
            var snippet = _runtime.Snippets.Find(_selectedSnippet.Folder, _selectedSnippet.Name);
            await OpenSnippetEditorAsync(_selectedSnippet, snippet?.Folder ?? _selectedSnippet.Folder, snippet?.Name ?? _selectedSnippet.Name, snippet?.Text ?? string.Empty);
        };
        _pasteSnippetButton.Click += (_, _) =>
        {
            if (_selectedSnippet is not null)
            {
                _runtime.PasteSnippet(_selectedSnippet.Folder, _selectedSnippet.Name);
            }
        };
        _deleteSnippetButton.Click += async (_, _) => await ConfirmAndDeleteSelectedSnippetAsync();
        _exportSnippetsButton.Click += async (_, _) => await ExportSnippetsAsync();
        _importSnippetsButton.Click += async (_, _) => await ImportSnippetsAsync();
        buttons.Children.Add(_newSnippetButton);
        buttons.Children.Add(_saveSnippetButton);
        buttons.Children.Add(_pasteSnippetButton);
        buttons.Children.Add(_deleteSnippetButton);
        details.Children.Add(buttons);
        var detailsCard = Card(details);
        Grid.SetColumn(detailsCard, 1);
        grid.Children.Add(detailsCard);
        _snippetPage.Children.Add(grid);
    }

    private void BuildAboutPage()
    {
        _aboutPage.Children.Add(PageHeader(_aboutHeaderText, _aboutDescriptionText));

        var info = new StackPanel { Spacing = 10 };
        info.Children.Add(InfoRow("Product", "Clipton"));
        info.Children.Add(InfoRow("Version", _runtime.AppVersion));
        info.Children.Add(_runtime.PackageStatus == "Unpackaged"
            ? InfoRow("Package", string.Empty, "PackageUnpackaged")
            : InfoRow("Package", _runtime.PackageStatus));
        info.Children.Add(InfoLinkRow("Author", AuthorUrl, AuthorUrl));
        _aboutPage.Children.Add(Card(info));

        var documents = new StackPanel { Spacing = 10 };
        documents.Children.Add(DescriptionText("LegalDescription",
            fontSize: 14,
            wrapping: TextWrapping.Wrap));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, Spacing = 8 };
        _termsButton.Click += (_, _) => OpenExternalUrl(TermsUrl);
        _privacyButton.Click += (_, _) => OpenExternalUrl(PrivacyUrl);
        buttons.Children.Add(_termsButton);
        buttons.Children.Add(_privacyButton);
        documents.Children.Add(buttons);
        _aboutPage.Children.Add(Card(documents));

        _exitApplicationButton.HorizontalAlignment = HorizontalAlignment.Left;
        _exitApplicationButton.Click += (_, _) => _runtime.ExitApplication();
        _aboutPage.Children.Add(SettingCard("\uE8BB", "ExitApplication", "ExitApplicationDescription", _exitApplicationButton));
    }

    private void BuildDonationPage()
    {
        _donationPage.Children.Add(PageHeader(_donationHeaderText, _donationDescriptionText));

        var content = new StackPanel { Spacing = 14 };
        content.Children.Add(DescriptionText("DonationSupportMessage",
            fontSize: 14,
            wrapping: TextWrapping.Wrap));

        var bannerImage = new Image
        {
            Source = new BitmapImage(new Uri(DonationBannerUrl)),
            Width = 217,
            Height = 60,
            Stretch = Stretch.Uniform
        };
        _donationFallbackText.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        _donationFallbackText.Padding = new Thickness(18, 12, 18, 12);
        _donationFallbackText.Text = _runtime.Translate("BuyMeACoffee");
        bannerImage.ImageFailed += (_, _) => _donationButton.Content = _donationFallbackText;

        _donationButton.Padding = new Thickness(0);
        _donationButton.BorderThickness = new Thickness(0);
        _donationButton.Background = new SolidColorBrush(Colors.Transparent);
        _donationButton.HorizontalAlignment = HorizontalAlignment.Left;
        _donationButton.Content = bannerImage;
        _donationButton.Click += (_, _) => OpenExternalUrl(DonationUrl);
        AutomationProperties.SetName(_donationButton, _runtime.Translate("BuyMeACoffee"));
        ToolTipService.SetToolTip(_donationButton, DonationUrl);
        content.Children.Add(_donationButton);

        _donationLinkButton.Content = _runtime.Translate("DonationOpenBuyMeACoffee");
        _donationLinkButton.Padding = new Thickness(0);
        _donationLinkButton.HorizontalAlignment = HorizontalAlignment.Left;
        AutomationProperties.SetName(_donationLinkButton, _runtime.Translate("DonationOpenBuyMeACoffee"));
        ToolTipService.SetToolTip(_donationLinkButton, DonationUrl);
        _donationLinkButton.Click += (_, _) => OpenExternalUrl(DonationUrl);

        var urlText = TrackDescriptionText(new TextBlock
        {
            Text = DonationUrl,
            FontSize = 12,
            Foreground = DescriptionBrush(),
            TextWrapping = TextWrapping.Wrap
        });

        content.Children.Add(new StackPanel
        {
            Spacing = 2,
            Margin = new Thickness(1, -6, 0, 0),
            Children =
            {
                _donationLinkButton,
                urlText
            }
        });
        _donationPage.Children.Add(Card(content));
    }

    private static void OpenExternalUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            ClipboardBridge.PutText(url);
        }
    }

    private async Task SetStartupAsync()
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            await RunOnUiThreadAsync(SetStartupAsync);
            return;
        }

        if (_loading || _startupRegistrationSaving) return;
        _startupRegistrationSaving = true;
        _startupToggle.IsEnabled = false;
        var requested = _startupToggle.IsOn;
        var previous = _runtime.Settings.StartWithWindows;
        Exception? startupException = null;
        try
        {
            await _runtime.SetStartWithWindowsAsync(requested).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            startupException = exception;
            AppDiagnostics.Log(exception, "Startup registration");
            _runtime.Settings.StartWithWindows = previous;
        }
        finally
        {
            await RunOnUiThreadAsync(() =>
            {
                _loading = true;
                try
                {
                    _startupToggle.IsOn = startupException is null
                        ? _runtime.Settings.StartWithWindows
                        : previous;
                }
                finally
                {
                    _loading = false;
                    _startupToggle.IsEnabled = !_runtime.IsSafeMode;
                    _startupRegistrationSaving = false;
                    RefreshTexts();
                }
            });
        }
    }

    private async Task SetStartupAsyncSafely()
    {
        try
        {
            await SetStartupAsync();
        }
        catch (Exception exception)
        {
            AppDiagnostics.Log(exception, "Startup toggle");
            try
            {
                await RunOnUiThreadAsync(() =>
                {
                    _loading = true;
                    try
                    {
                        _startupToggle.IsOn = _runtime.Settings.StartWithWindows;
                    }
                    finally
                    {
                        _loading = false;
                        _startupToggle.IsEnabled = !_runtime.IsSafeMode;
                        _startupRegistrationSaving = false;
                    }
                });
            }
            catch (Exception recoveryException)
            {
                AppDiagnostics.Log(recoveryException, "Startup toggle recovery");
            }
        }
    }

    private Task RunOnUiThreadAsync(Action action)
    {
        if (DispatcherQueue.HasThreadAccess)
        {
            action();
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                action();
                completion.SetResult();
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        }))
        {
            completion.SetException(new InvalidOperationException("Unable to enqueue UI work."));
        }

        return completion.Task;
    }

    private Task RunOnUiThreadAsync(Func<Task> action)
    {
        if (DispatcherQueue.HasThreadAccess)
        {
            return action();
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                await action();
                completion.SetResult();
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        }))
        {
            completion.SetException(new InvalidOperationException("Unable to enqueue UI work."));
        }

        return completion.Task;
    }

    private void SaveStartupWindowOptions()
    {
        if (_loading) return;
        _runtime.SetHideSettingsWindowOnStartup(_hideSettingsWindowOnStartupToggle.IsOn);
    }

    private async Task TryApplyHotkeyAsync(string hotkey)
    {
        if (_loading)
        {
            return;
        }

        if (_runtime.SetHotkey(hotkey))
        {
            RefreshTexts();
            return;
        }

        await ShowMessageDialogAsync(
            _runtime.Translate("Hotkey"),
            _runtime.Translate("HotkeyUnavailable"),
            _runtime.Translate("Close"));
        RefreshTexts();
    }

    private async Task ConfirmAndResetHotkeyAsync()
    {
        var defaultHotkey = HotkeyGesture.Default.ToString();
        if (string.Equals(_runtime.Settings.Hotkey, defaultHotkey, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var confirmed = await ConfirmDialogAsync(
            _runtime.Translate("ConfirmResetHotkeyTitle"),
            string.Format(_runtime.Translate("ConfirmResetHotkeyMessage"), defaultHotkey),
            _runtime.Translate("ResetHotkey"),
            _runtime.Translate("Cancel"));
        if (!confirmed)
        {
            return;
        }

        await TryApplyHotkeyAsync(defaultHotkey);
    }

    private async Task CaptureCustomHotkeyAsync()
    {
        var hotkey = await PromptForHotkeyAsync();
        if (hotkey is not null)
        {
            await TryApplyHotkeyAsync(hotkey);
        }
    }

    private async Task SaveHistoryOptionsAsync()
    {
        if (_loading || _historyOptionsSaving) return;
        _historyOptionsSaving = true;
        try
        {
            if (_runtime.Settings.PersistEncryptedHistory && !_persistHistoryToggle.IsOn)
            {
                var confirmed = await ConfirmDialogAsync(
                    _runtime.Translate("ConfirmDisablePersistHistoryTitle"),
                    _runtime.Translate("ConfirmDisablePersistHistoryMessage"),
                    _runtime.Translate("DisablePersistHistory"),
                    _runtime.Translate("Cancel"));
                if (!confirmed)
                {
                    _loading = true;
                    try
                    {
                        _persistHistoryToggle.IsOn = true;
                    }
                    finally
                    {
                        _loading = false;
                    }

                    return;
                }
            }

            if (_runtime.Settings.PauseCapture != _pauseCaptureToggle.IsOn)
            {
                _runtime.SetPauseCapture(_pauseCaptureToggle.IsOn);
            }

            if (_runtime.Settings.PersistEncryptedHistory != _persistHistoryToggle.IsOn)
            {
                _runtime.SetPersistEncryptedHistory(_persistHistoryToggle.IsOn);
            }

            if (_runtime.Settings.MaskSensitiveContent != _maskSensitiveContentToggle.IsOn)
            {
                _runtime.SetMaskSensitiveContent(_maskSensitiveContentToggle.IsOn);
            }

            RefreshItems();
        }
        finally
        {
            _historyOptionsSaving = false;
        }
    }

    private async Task ChangeHistoryAccessLockEnabledAsync()
    {
        if (_loading)
        {
            return;
        }

        var requested = _historyAccessLockToggle.IsOn;
        if (requested)
        {
            if (!_runtime.IsHistoryAccessLockConfigured)
            {
                var pin = await PromptForNewHistoryAccessPinAsync();
                if (pin is null)
                {
                    ResetHistoryAccessLockControls();
                    return;
                }

                _runtime.ConfigureHistoryAccessLock(pin, GetSelectedHistoryAccessLockTimeout());
            }
            else
            {
                _runtime.SetHistoryAccessLockEnabled(true);
            }
        }
        else
        {
            if (_runtime.IsHistoryAccessLockEnabled && !await PromptForHistoryAccessPinAsync(_runtime.Translate("DisableHistoryAccessLock")))
            {
                ResetHistoryAccessLockControls();
                return;
            }

            _runtime.SetHistoryAccessLockEnabled(false);
        }

        UpdateHistoryAccessLockUi();
    }

    private async Task ChangeHistoryAccessPinAsync()
    {
        if (_runtime.IsHistoryAccessLockConfigured
            && !await PromptForHistoryAccessPinAsync(_runtime.Translate("ChangePin")))
        {
            return;
        }

        var pin = await PromptForNewHistoryAccessPinAsync();
        if (pin is null)
        {
            return;
        }

        _runtime.ConfigureHistoryAccessLock(pin, GetSelectedHistoryAccessLockTimeout());
        ResetHistoryAccessLockControls();
    }

    private async Task ConfirmAndResetHistoryAccessLockAsync()
    {
        var confirmed = await ConfirmDialogAsync(
            _runtime.Translate("ConfirmResetHistoryAccessLockTitle"),
            _runtime.Translate("ConfirmResetHistoryAccessLockMessage"),
            _runtime.Translate("ResetPinAndClearHistory"),
            _runtime.Translate("Cancel"));
        if (!confirmed)
        {
            return;
        }

        _runtime.ResetHistoryAccessLockAndClearProtectedData();
        ResetHistoryAccessLockControls();
    }

    private void ChangeHistoryAccessLockTimeout()
    {
        if (_loading)
        {
            return;
        }

        _runtime.SetHistoryAccessLockTimeoutMinutes(GetSelectedHistoryAccessLockTimeout());
        UpdateHistoryAccessLockUi();
    }

    private int GetSelectedHistoryAccessLockTimeout()
    {
        return _historyAccessLockTimeoutBox.SelectedItem is ComboBoxItem { Tag: string tag } && int.TryParse(tag, out var parsed)
            ? HistoryAccessLockCredential.NormalizeTimeoutMinutes(parsed)
            : _runtime.Settings.HistoryAccessLockTimeoutMinutes;
    }

    private void ResetHistoryAccessLockControls()
    {
        _loading = true;
        try
        {
            _historyAccessLockToggle.IsOn = _runtime.Settings.HistoryAccessLockEnabled && _runtime.IsHistoryAccessLockConfigured;
            SetComboSelection(_historyAccessLockTimeoutBox, _runtime.Settings.HistoryAccessLockTimeoutMinutes.ToString(), refreshSelectionBox: true);
        }
        finally
        {
            _loading = false;
        }

        UpdateHistoryAccessLockUi();
    }

    private void UpdateHistoryAccessLockUi()
    {
        var configured = _runtime.IsHistoryAccessLockConfigured;
        var enabled = _runtime.IsHistoryAccessLockEnabled;
        SetCommandButton(_historyAccessLockPinButton, "\uE72E", configured ? _runtime.Translate("ChangePin") : _runtime.Translate("SetPin"));
        SetCommandButton(_historyAccessLockResetButton, "\uE777", _runtime.Translate("ResetPin"));
        SetCommandButton(_historyAccessLockNowButton, "\uE785", _runtime.Translate("LockNow"));
        _historyAccessLockTimeoutBox.IsEnabled = configured;
        _historyAccessLockResetButton.IsEnabled = configured;
        _historyAccessLockNowButton.IsEnabled = enabled;
        _historyAccessLockStatusText.Text = !configured
            ? _runtime.Translate("HistoryAccessLockStatusNotConfigured")
            : enabled
                ? _runtime.Translate("HistoryAccessLockStatusEnabled")
                : _runtime.Translate("HistoryAccessLockStatusDisabled");
    }

    private void ChangeQuickMenuTopLevelHistoryItems()
    {
        if (_loading || _quickMenuTopLevelHistoryItemsBox.SelectedItem is not ComboBoxItem selected || selected.Tag is not string tag)
        {
            return;
        }

        var count = int.TryParse(tag, out var parsed) ? parsed : _runtime.Settings.QuickMenuTopLevelHistoryItems;
        if (_runtime.Settings.QuickMenuTopLevelHistoryItems == count)
        {
            return;
        }

        _runtime.SetQuickMenuTopLevelHistoryItems(count);
    }

    private void SaveDiagnosticLogging()
    {
        if (_loading) return;
        _runtime.SetDiagnosticLogging(_diagnosticLoggingToggle.IsOn);
    }

    private void UpdateDataDirectoryText()
    {
        var configured = _runtime.ConfiguredDataDirectory;
        if (string.IsNullOrWhiteSpace(configured))
        {
            _dataDirectoryText.Text = $"{_runtime.Translate("DataDirectoryDefault")}\n{_runtime.DataDirectory}";
            return;
        }

        _dataDirectoryText.Text = string.Equals(configured, _runtime.DataDirectory, StringComparison.OrdinalIgnoreCase)
            ? $"{_runtime.Translate("DataDirectoryCustom")}\n{configured}"
            : $"{_runtime.Translate("DataDirectoryCustom")}\n{configured}\n{string.Format(_runtime.Translate("DataDirectoryCurrent"), _runtime.DataDirectory)}";
    }

    private async Task ChangeDataDirectoryAsync()
    {
        var path = await SelectFolderAsync();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        await SaveDataDirectoryAsync(path);
    }

    private async Task ResetDataDirectoryAsync()
    {
        await SaveDataDirectoryAsync(null);
    }

    private async Task SaveDataDirectoryAsync(string? path)
    {
        try
        {
            _runtime.SetConfiguredDataDirectory(path);
            UpdateDataDirectoryText();
            await ShowMessageDialogAsync(
                _runtime.Translate("DataDirectoryChangedTitle"),
                _runtime.Translate("DataDirectoryChangedMessage"),
                _runtime.Translate("Close"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            await ShowMessageDialogAsync(
                _runtime.Translate("DataDirectory"),
                string.Format(_runtime.Translate("DataDirectoryChangeFailed"), ex.Message),
                _runtime.Translate("Close"));
        }
    }

    private async Task ConfirmAndClearHistoryAsync()
    {
        var confirmed = await ConfirmDialogAsync(
            _runtime.Translate("ConfirmClearHistoryTitle"),
            _runtime.Translate("ConfirmClearHistoryMessage"),
            _runtime.Translate("ClearHistory"),
            _runtime.Translate("Cancel"));
        if (!confirmed)
        {
            return;
        }

        _runtime.ClearHistory();
    }

    private async Task ConfirmAndClearLogsAsync()
    {
        var confirmed = await ConfirmDialogAsync(
            _runtime.Translate("ConfirmClearLogsTitle"),
            _runtime.Translate("ConfirmClearLogsMessage"),
            _runtime.Translate("ClearLogs"),
            _runtime.Translate("Cancel"));
        if (!confirmed)
        {
            return;
        }

        _runtime.ClearDiagnosticLogs();
    }

    private async Task ConfirmAndDeleteSelectedSnippetAsync()
    {
        if (_selectedSnippet is null || _snippetDeleteInProgress)
        {
            return;
        }

        _snippetDeleteInProgress = true;
        try
        {
            var selected = _selectedSnippet;
            var confirmed = await ConfirmDialogAsync(
                _runtime.Translate("ConfirmDeleteSnippetTitle"),
                string.Format(_runtime.Translate("ConfirmDeleteSnippetMessage"), selected.DisplayName),
                _runtime.Translate("Delete"),
                _runtime.Translate("Cancel"));
            if (!confirmed)
            {
                return;
            }

            _runtime.RemoveSnippet(selected.Folder, selected.Name);
            if (_selectedSnippet is not null
                && string.Equals(_selectedSnippet.Folder, selected.Folder, StringComparison.Ordinal)
                && string.Equals(_selectedSnippet.Name, selected.Name, StringComparison.Ordinal))
            {
                _selectedSnippet = null;
            }

            UpdateSelectedSnippetText();
            RefreshItems();
        }
        finally
        {
            _snippetDeleteInProgress = false;
        }
    }

    private async Task ConfirmAndDeleteHistoryItemAsync(string id)
    {
        if (_historyItemDeleteInProgress)
        {
            return;
        }

        _historyItemDeleteInProgress = true;
        try
        {
            var confirmed = await ConfirmDialogAsync(
                _runtime.Translate("ConfirmDeleteHistoryItemTitle"),
                _runtime.Translate("ConfirmDeleteHistoryItemMessage"),
                _runtime.Translate("Delete"),
                _runtime.Translate("Cancel"));
            if (!confirmed)
            {
                return;
            }

            _runtime.RemoveHistoryItem(id);
            if (string.Equals(_selectedHistoryId, id, StringComparison.Ordinal))
            {
                _selectedHistoryId = null;
            }

            RefreshItems();
        }
        finally
        {
            _historyItemDeleteInProgress = false;
        }
    }

    private async Task ChangeMaxHistoryItemsAsync()
    {
        if (_loading || _maxHistoryItemsBox.SelectedItem is not ComboBoxItem selected || selected.Tag is not string tag)
        {
            return;
        }

        var count = int.TryParse(tag, out var parsed) ? Math.Clamp(parsed, 1, 1000) : _runtime.Settings.MaxHistoryItems;
        if (_runtime.Settings.MaxHistoryItems == count)
        {
            return;
        }

        var currentHistoryCount = _runtime.History.Items.Count;
        if (count < currentHistoryCount)
        {
            var confirmed = await ConfirmDialogAsync(
                _runtime.Translate("ConfirmReduceHistoryLimitTitle"),
                string.Format(_runtime.Translate("ConfirmReduceHistoryLimitMessage"), currentHistoryCount - count, count),
                _runtime.Translate("Apply"),
                _runtime.Translate("Cancel"));
            if (!confirmed)
            {
                _loading = true;
                try
                {
                    SetComboSelection(_maxHistoryItemsBox, _runtime.Settings.MaxHistoryItems.ToString());
                }
                finally
                {
                    _loading = false;
                }

                return;
            }
        }

        _runtime.SetMaxHistoryItems(count);
    }

    private void ChangeClipboardCaptureDelay()
    {
        if (_loading || _clipboardCaptureDelayBox.SelectedItem is not ComboBoxItem selected || selected.Tag is not string tag)
        {
            return;
        }

        var delay = int.TryParse(tag, out var parsed) ? parsed : _runtime.Settings.ClipboardCaptureDelayMilliseconds;
        if (_runtime.Settings.ClipboardCaptureDelayMilliseconds == delay)
        {
            return;
        }

        _runtime.SetClipboardCaptureDelay(delay);
    }

    private void ChangeQuickMenuImagePreviewSize()
    {
        if (_loading || _quickMenuImagePreviewSizeBox.SelectedItem is not ComboBoxItem selected || selected.Tag is not string size)
        {
            return;
        }

        _runtime.SetQuickMenuImagePreviewSize(size);
    }

    private void ChangeQuickMenuDisplayMode()
    {
        if (_loading || _quickMenuDisplayModeBox.SelectedItem is not ComboBoxItem selected || selected.Tag is not string mode)
        {
            return;
        }

        _runtime.SetQuickMenuDisplayMode(mode);
    }

    private void SaveQuickMenuDisplayOptions()
    {
        if (_loading) return;
        _runtime.SetQuickMenuShowCapturedAt(_quickMenuShowCapturedAtToggle.IsOn);
        _runtime.SetQuickMenuShowShortcutHints(_quickMenuShowShortcutHintsToggle.IsOn);
    }

    private void RefreshQuickMenuPasteOptionChecks()
    {
        foreach (var (optionId, entry) in _quickMenuPasteOptionChecks)
        {
            var label = _runtime.Translate(entry.LabelKey);
            entry.CheckBox.IsChecked = _runtime.IsQuickMenuPasteOptionEnabled(optionId);
            AutomationProperties.SetName(entry.CheckBox, label);
            ToolTipService.SetToolTip(entry.CheckBox, label);
        }
    }

    private void SaveQuickMenuPasteOption(CheckBox checkBox, string optionId)
    {
        if (_loading)
        {
            return;
        }

        _runtime.SetQuickMenuPasteOptionEnabled(optionId, checkBox.IsChecked == true);
        RefreshItems();
    }

    private void ChangeQuickMenuShortcut(string action, ComboBox comboBox)
    {
        if (_loading || comboBox.SelectedItem is not ComboBoxItem selected || selected.Tag is not string shortcut)
        {
            return;
        }

        _runtime.SetQuickMenuShortcut(action, shortcut);
    }

    private void RegisterSelectedHistory()
    {
        if (_selectedHistoryId is null) return;
        var item = _runtime.History.Find(_selectedHistoryId);
        if (string.IsNullOrWhiteSpace(item?.Text)) return;
        SelectPage(4);
        _ = OpenSnippetEditorAsync(null, "History", CreateSnippetName(item.Text), item.Text);
    }

    private async Task ExportHistoryAsync()
    {
        var path = await SelectExportPathAsync($"clipton-history-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        if (path is null)
        {
            return;
        }

        if (!await ConfirmProtectedDataExportAsync("ConfirmExportHistoryMessage"))
        {
            return;
        }

        await RunImportExportActionAsync(
            "ExportHistory",
            () => string.Format(_runtime.Translate("ExportComplete"), _runtime.ExportHistory(path)));
    }

    private async Task ImportHistoryAsync()
    {
        var path = await SelectImportPathAsync();
        if (path is null)
        {
            return;
        }

        HistoryImportPreview preview;
        try
        {
            preview = _runtime.PreviewImportHistory(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException)
        {
            await ShowMessageDialogAsync(
                _runtime.Translate("ImportHistory"),
                string.Format(_runtime.Translate("ImportExportFailed"), ex.Message),
                _runtime.Translate("Close"));
            return;
        }

        if (!await ConfirmDialogAsync(
                _runtime.Translate("ConfirmImportHistoryTitle"),
                string.Format(
                    _runtime.Translate("ConfirmImportHistoryPreviewMessage"),
                    preview.SourceItems,
                    preview.UniqueItems,
                    preview.ReplacementItems,
                    preview.RemovedByCapacityItems,
                    preview.Capacity),
                _runtime.Translate("ImportHistory"),
                _runtime.Translate("Cancel")))
        {
            return;
        }

        await RunImportExportActionAsync(
            "ImportHistory",
            () =>
            {
                var count = _runtime.ImportHistory(path);
                RefreshItems();
                return string.Format(_runtime.Translate("ImportComplete"), count);
            });
    }

    private async Task ExportSnippetsAsync()
    {
        var path = await SelectExportPathAsync($"clipton-snippets-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        if (path is null)
        {
            return;
        }

        if (!await ConfirmProtectedDataExportAsync("ConfirmExportSnippetsMessage"))
        {
            return;
        }

        await RunImportExportActionAsync(
            "ExportSnippets",
            () => string.Format(_runtime.Translate("ExportComplete"), _runtime.ExportSnippets(path)));
    }

    private async Task<bool> ConfirmProtectedDataExportAsync(string messageKey)
    {
        if (_root.XamlRoot is null)
        {
            return false;
        }

        var title = _runtime.Translate("ConfirmExportProtectedDataTitle");
        var message = _runtime.Translate(messageKey);
        if (!_runtime.IsHistoryAccessLockEnabled)
        {
            return await ConfirmDialogAsync(
                title,
                message,
                _runtime.Translate("Export"),
                _runtime.Translate("Cancel"));
        }

        var pinBox = new PasswordBox
        {
            MaxLength = HistoryAccessLockCredential.MaxPinLength,
            PlaceholderText = _runtime.Translate("HistoryAccessPinPlaceholder")
        };
        var errorText = Description();
        errorText.Foreground = new SolidColorBrush(Colors.IndianRed);
        errorText.TextWrapping = TextWrapping.Wrap;
        var content = new StackPanel
        {
            Spacing = 10,
            MaxWidth = 420,
            Children =
            {
                new TextBlock
                {
                    Text = message,
                    TextWrapping = TextWrapping.Wrap
                },
                new TextBlock
                {
                    Text = _runtime.Translate("ConfirmExportProtectedDataPinDescription"),
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = DescriptionBrush()
                },
                pinBox,
                errorText
            }
        };

        var confirmed = false;
        var dialog = new ContentDialog
        {
            Title = title,
            Content = content,
            PrimaryButtonText = _runtime.Translate("Export"),
            CloseButtonText = _runtime.Translate("Cancel"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = _root.XamlRoot,
            RequestedTheme = IsDark ? ElementTheme.Dark : ElementTheme.Light
        };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (!_runtime.UnlockHistoryAccess(pinBox.Password.Trim()))
            {
                errorText.Text = _runtime.Translate("HistoryAccessPinInvalid");
                pinBox.Password = string.Empty;
                pinBox.Focus(FocusState.Programmatic);
                args.Cancel = true;
                return;
            }

            confirmed = true;
        };

        pinBox.Loaded += (_, _) => pinBox.Focus(FocusState.Programmatic);
        await ShowContentDialogAsync(dialog);
        UpdateHistoryAccessLockUi();
        return confirmed;
    }

    private async Task ImportSnippetsAsync()
    {
        var path = await SelectImportPathAsync();
        if (path is null)
        {
            return;
        }

        SnippetImportPreview preview;
        try
        {
            preview = _runtime.PreviewImportSnippets(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException)
        {
            await ShowMessageDialogAsync(
                _runtime.Translate("ImportSnippets"),
                string.Format(_runtime.Translate("ImportExportFailed"), ex.Message),
                _runtime.Translate("Close"));
            return;
        }

        if (!await ConfirmDialogAsync(
                _runtime.Translate("ConfirmImportSnippetsTitle"),
                string.Format(
                    _runtime.Translate("ConfirmImportSnippetsPreviewMessage"),
                    preview.SourceItems,
                    preview.ValidItems,
                    preview.ReplacementItems),
                _runtime.Translate("ImportSnippets"),
                _runtime.Translate("Cancel")))
        {
            return;
        }

        await RunImportExportActionAsync(
            "ImportSnippets",
            () =>
            {
                var count = _runtime.ImportSnippets(path);
                _selectedSnippet = null;
                UpdateSelectedSnippetText();
                RefreshItems();
                return string.Format(_runtime.Translate("ImportComplete"), count);
            });
    }

    private async Task<string?> SelectExportPathAsync(string fileName)
    {
        var picker = new FileSavePicker
        {
            SuggestedFileName = fileName,
            DefaultFileExtension = ".json",
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary
        };
        picker.FileTypeChoices.Add(_runtime.Translate("JsonFiles"), [".json"]);
        InitializeWithWindow.Initialize(picker, _hwnd);
        var file = await picker.PickSaveFileAsync();
        return file?.Path;
    }

    private async Task<string?> SelectImportPathAsync()
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary
        };
        picker.FileTypeFilter.Add(".json");
        InitializeWithWindow.Initialize(picker, _hwnd);
        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    private async Task<string?> SelectFolderAsync()
    {
        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary
        };
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, _hwnd);
        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    private async Task RunImportExportActionAsync(string titleKey, Func<string> action)
    {
        try
        {
            await ShowMessageDialogAsync(
                _runtime.Translate(titleKey),
                action(),
                _runtime.Translate("Close"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException)
        {
            await ShowMessageDialogAsync(
                _runtime.Translate(titleKey),
                string.Format(_runtime.Translate("ImportExportFailed"), ex.Message),
                _runtime.Translate("Close"));
        }
    }

    private async Task<ContentDialogResult> ShowContentDialogAsync(ContentDialog dialog)
    {
        await _dialogGate.WaitAsync();
        try
        {
            return await dialog.ShowAsync();
        }
        finally
        {
            _dialogGate.Release();
        }
    }

    private async Task ShowMessageDialogAsync(string title, string message, string closeText)
    {
        if (_root.XamlRoot is null)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap
            },
            CloseButtonText = closeText,
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = _root.XamlRoot,
            RequestedTheme = IsDark ? ElementTheme.Dark : ElementTheme.Light
        };
        await ShowContentDialogAsync(dialog);
    }

    private async Task<bool> PromptForHistoryAccessUnlockAsync()
    {
        return await PromptForHistoryAccessPinAsync(_runtime.Translate("UnlockHistoryAccess"));
    }

    private async Task<bool> PromptForHistoryAccessPinAsync(string title)
    {
        if (_root.XamlRoot is null)
        {
            return false;
        }

        var pinBox = new PasswordBox
        {
            MaxLength = HistoryAccessLockCredential.MaxPinLength,
            PlaceholderText = _runtime.Translate("HistoryAccessPinPlaceholder")
        };
        var errorText = Description();
        errorText.Foreground = new SolidColorBrush(Colors.IndianRed);
        errorText.TextWrapping = TextWrapping.Wrap;
        var content = new StackPanel
        {
            Spacing = 10,
            MaxWidth = 360,
            Children =
            {
                new TextBlock
                {
                    Text = _runtime.Translate("HistoryAccessUnlockDescription"),
                    TextWrapping = TextWrapping.Wrap
                },
                pinBox,
                errorText
            }
        };

        var unlocked = false;
        var dialog = new ContentDialog
        {
            Title = title,
            Content = content,
            PrimaryButtonText = _runtime.Translate("Unlock"),
            CloseButtonText = _runtime.Translate("Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = _root.XamlRoot,
            RequestedTheme = IsDark ? ElementTheme.Dark : ElementTheme.Light
        };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (!_runtime.UnlockHistoryAccess(pinBox.Password.Trim()))
            {
                errorText.Text = _runtime.Translate("HistoryAccessPinInvalid");
                pinBox.Password = string.Empty;
                pinBox.Focus(FocusState.Programmatic);
                args.Cancel = true;
                return;
            }

            unlocked = true;
        };

        pinBox.Loaded += (_, _) => pinBox.Focus(FocusState.Programmatic);
        await ShowContentDialogAsync(dialog);
        UpdateHistoryAccessLockUi();
        return unlocked;
    }

    private async Task<string?> PromptForNewHistoryAccessPinAsync()
    {
        if (_root.XamlRoot is null)
        {
            return null;
        }

        var pinBox = new PasswordBox
        {
            MaxLength = HistoryAccessLockCredential.MaxPinLength,
            PlaceholderText = _runtime.Translate("HistoryAccessPinPlaceholder")
        };
        var confirmBox = new PasswordBox
        {
            MaxLength = HistoryAccessLockCredential.MaxPinLength,
            PlaceholderText = _runtime.Translate("HistoryAccessPinConfirmPlaceholder")
        };
        var errorText = Description();
        errorText.Foreground = new SolidColorBrush(Colors.IndianRed);
        errorText.TextWrapping = TextWrapping.Wrap;
        var content = new StackPanel
        {
            Spacing = 10,
            MaxWidth = 380,
            Children =
            {
                new TextBlock
                {
                    Text = _runtime.Translate("SetHistoryAccessPinDescription"),
                    TextWrapping = TextWrapping.Wrap
                },
                pinBox,
                confirmBox,
                errorText
            }
        };

        string? captured = null;
        var dialog = new ContentDialog
        {
            Title = _runtime.Translate("SetPin"),
            Content = content,
            PrimaryButtonText = _runtime.Translate("Save"),
            CloseButtonText = _runtime.Translate("Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = _root.XamlRoot,
            RequestedTheme = IsDark ? ElementTheme.Dark : ElementTheme.Light
        };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            var pin = pinBox.Password.Trim();
            var confirm = confirmBox.Password.Trim();
            if (!HistoryAccessLockCredential.IsValidPin(pin))
            {
                errorText.Text = _runtime.Translate("HistoryAccessPinFormatInvalid");
                pinBox.Password = string.Empty;
                confirmBox.Password = string.Empty;
                pinBox.Focus(FocusState.Programmatic);
                args.Cancel = true;
                return;
            }

            if (!string.Equals(pin, confirm, StringComparison.Ordinal))
            {
                errorText.Text = _runtime.Translate("HistoryAccessPinMismatch");
                confirmBox.Password = string.Empty;
                confirmBox.Focus(FocusState.Programmatic);
                args.Cancel = true;
                return;
            }

            captured = pin;
        };

        pinBox.Loaded += (_, _) => pinBox.Focus(FocusState.Programmatic);
        await ShowContentDialogAsync(dialog);
        return captured;
    }

    private async Task ShowOnboardingDialogAsync()
    {
        if (_root.XamlRoot is null || _runtime.Settings.InitialLaunchCompleted)
        {
            return;
        }

        var content = new StackPanel { Spacing = 14, MaxWidth = 560 };
        content.Children.Add(new TextBlock
        {
            Text = _runtime.Translate("OnboardingDescription"),
            TextWrapping = TextWrapping.Wrap
        });

        var points = new StackPanel { Spacing = 8 };
        foreach (var key in new[] { "OnboardingPointLocal", "OnboardingPointPrivacy", "OnboardingPointControl" })
        {
            points.Children.Add(new TextBlock
            {
                Text = $"• {_runtime.Translate(key)}",
                TextWrapping = TextWrapping.Wrap
            });
        }

        content.Children.Add(points);
        content.Children.Add(new TextBlock
        {
            Text = _runtime.Translate("OnboardingAgreementNotice"),
            TextWrapping = TextWrapping.Wrap,
            Foreground = DescriptionBrush()
        });

        var links = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var termsButton = new Button { Content = _runtime.Translate("TermsOfUse") };
        var privacyButton = new Button { Content = _runtime.Translate("PrivacyPolicy") };
        termsButton.Click += (_, _) => OpenExternalUrl(TermsUrl);
        privacyButton.Click += (_, _) => OpenExternalUrl(PrivacyUrl);
        links.Children.Add(termsButton);
        links.Children.Add(privacyButton);
        content.Children.Add(links);

        var dialog = new ContentDialog
        {
            Title = _runtime.Translate("OnboardingTitle"),
            Content = content,
            PrimaryButtonText = _runtime.Translate("StartUsing"),
            CloseButtonText = _runtime.Translate("Exit"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = _root.XamlRoot,
            RequestedTheme = IsDark ? ElementTheme.Dark : ElementTheme.Light
        };

        var result = await ShowContentDialogAsync(dialog);
        if (result == ContentDialogResult.Primary)
        {
            _runtime.CompleteOnboarding();
            return;
        }

        _runtime.ExitApplication();
    }

    private async Task<bool> ConfirmDialogAsync(string title, string message, string primaryText, string closeText)
    {
        if (_root.XamlRoot is null)
        {
            return false;
        }

        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap
            },
            PrimaryButtonText = primaryText,
            CloseButtonText = closeText,
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = _root.XamlRoot,
            RequestedTheme = IsDark ? ElementTheme.Dark : ElementTheme.Light
        };
        return await ShowContentDialogAsync(dialog) == ContentDialogResult.Primary;
    }

    private async Task ShowHistoryImagePreviewAsync(HistoryItemViewModel item)
    {
        var imageBytes = _runtime.GetHistoryImagePreviewBytes(item.Id);
        if (_root.XamlRoot is null
            || imageBytes is not { Length: > 0 })
        {
            return;
        }

        var image = new Image
        {
            Source = await CreateBitmapImageAsync(imageBytes),
            Stretch = Stretch.Uniform,
            MaxWidth = 760,
            MaxHeight = 560,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var frame = new Border
        {
            MinWidth = 360,
            MinHeight = 240,
            MaxWidth = 800,
            MaxHeight = 600,
            Padding = new Thickness(8),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            BorderBrush = CardBorderBrush(),
            Background = Brush(IsDark ? "#111111" : "#F7F7F7"),
            Child = image
        };

        var dialog = new ContentDialog
        {
            Title = _runtime.Translate("ImagePreview"),
            Content = frame,
            CloseButtonText = _runtime.Translate("Close"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = _root.XamlRoot,
            RequestedTheme = IsDark ? ElementTheme.Dark : ElementTheme.Light
        };

        await ShowContentDialogAsync(dialog);
    }

    private void SelectSnippet(SnippetItemViewModel selected)
    {
        SetSelectedSnippet(selected);
        SelectSnippetTreeNode(selected);
    }

    private void SetSelectedSnippet(SnippetItemViewModel selected)
    {
        _selectedSnippet = selected;
        _selectedSnippetFolder = selected.Folder;
        UpdateSelectedSnippetText();
    }

    private void SetSelectedSnippetFolder(string folder)
    {
        _selectedSnippet = null;
        _selectedSnippetFolder = NormalizeSnippetFolder(folder);
        UpdateSelectedSnippetText();
    }

    private void UpdateSelectedSnippetText()
    {
        _selectedSnippetText.Text = _selectedSnippet is null
            ? string.IsNullOrWhiteSpace(_selectedSnippetFolder)
                ? _runtime.Translate("SnippetEditorEmpty")
                : string.Format(_runtime.Translate("SnippetFolderSelected"), _selectedSnippetFolder)
            : $"{_selectedSnippet.DisplayName}\n{_selectedSnippet.Folder}";

        var hasSnippetSelection = _selectedSnippet is not null;
        _saveSnippetButton.IsEnabled = hasSnippetSelection;
        _pasteSnippetButton.IsEnabled = hasSnippetSelection;
        _deleteSnippetButton.IsEnabled = hasSnippetSelection;
    }

    private void RefreshSnippetTree()
    {
        _updatingSnippetTreeSelection = true;
        _snippetNodes.Clear();
        _snippetFolderNodes.Clear();
        _snippetTree.RootNodes.Clear();

        var folderNodesByPath = new Dictionary<string, TreeViewNode>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in CollectSnippetFolders())
        {
            EnsureSnippetFolderNode(folder, folderNodesByPath);
        }

        foreach (var snippet in _runtime.Snippets.Snippets.OrderBy(snippet => snippet.Name, StringComparer.OrdinalIgnoreCase))
        {
            var item = SnippetItemViewModel.FromSnippet(snippet);
            var snippetNode = new TreeViewNode
            {
                Content = SnippetTreeRow("\uE8A5", item.Name, item.Folder, item)
            };
            _snippetNodes[snippetNode] = item;

            var folder = NormalizeSnippetFolder(snippet.Folder);
            if (!string.IsNullOrWhiteSpace(folder))
            {
                EnsureSnippetFolderNode(folder, folderNodesByPath).Children.Add(snippetNode);
            }
            else
            {
                _snippetTree.RootNodes.Add(snippetNode);
            }
        }

        if (_snippetTree.RootNodes.Count == 0)
        {
            _snippetTree.RootNodes.Add(new TreeViewNode
            {
                Content = new TextBlock
                {
                    Text = _runtime.Translate("SnippetEditorEmpty"),
                    Foreground = DescriptionBrush(),
                    Margin = new Thickness(8, 6, 8, 6)
                }
            });
        }

        _updatingSnippetTreeSelection = false;
        if (_selectedSnippet is not null)
        {
            SelectSnippetTreeNode(_selectedSnippet);
        }
        else if (!string.IsNullOrWhiteSpace(_selectedSnippetFolder))
        {
            SelectSnippetFolderTreeNode(_selectedSnippetFolder);
        }
    }

    private IEnumerable<string> CollectSnippetFolders()
    {
        return _runtime.Snippets.Snippets
            .Select(snippet => snippet.Folder)
            .SelectMany(GetFolderAndParents)
            .Where(folder => !string.IsNullOrWhiteSpace(folder))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(folder => folder, StringComparer.OrdinalIgnoreCase);
    }

    private TreeViewNode EnsureSnippetFolderNode(string folder, Dictionary<string, TreeViewNode> nodesByPath)
    {
        var normalized = NormalizeSnippetFolder(folder);
        if (nodesByPath.TryGetValue(normalized, out var existing))
        {
            return existing;
        }

        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var title = parts.Length == 0 ? normalized : parts[^1];
        var node = new TreeViewNode
        {
            Content = SnippetTreeRow("\uE8B7", title, normalized),
            IsExpanded = true
        };
        _snippetFolderNodes[node] = normalized;
        nodesByPath[normalized] = node;

        var parent = GetParentFolder(normalized);
        if (string.IsNullOrEmpty(parent))
        {
            _snippetTree.RootNodes.Add(node);
        }
        else
        {
            EnsureSnippetFolderNode(parent, nodesByPath).Children.Add(node);
        }

        return node;
    }

    private UIElement SnippetTreeRow(string glyph, string title, string subtitle, SnippetItemViewModel? item = null)
    {
        var row = new Grid
        {
            ColumnSpacing = 10,
            Padding = new Thickness(2, 4, 2, 4),
            Tag = item
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.Children.Add(new FontIcon
        {
            Glyph = glyph,
            FontFamily = new FontFamily("Segoe Fluent Icons"),
            FontSize = item is null ? 15 : 16,
            Foreground = item is null ? DescriptionBrush() : Brush(IsDark ? "#DADADA" : "#4A4A4A"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });

        var texts = new StackPanel { Spacing = 1 };
        texts.Children.Add(new TextBlock
        {
            Text = title,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        });
        texts.Children.Add(new TextBlock
        {
            Text = subtitle,
            FontSize = 12,
            Foreground = DescriptionBrush(),
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        Grid.SetColumn(texts, 1);
        row.Children.Add(texts);

        if (item is not null)
        {
            row.Tapped += (_, _) => SelectSnippet(item);
            row.DoubleTapped += (_, _) => _runtime.PasteSnippet(item.Folder, item.Name);
        }

        return row;
    }

    private void SelectSnippetTreeNode(SnippetItemViewModel selected)
    {
        var node = _snippetNodes.FirstOrDefault(pair =>
            string.Equals(pair.Value.Folder, selected.Folder, StringComparison.Ordinal)
            && string.Equals(pair.Value.Name, selected.Name, StringComparison.Ordinal)).Key;
        if (node is null)
        {
            return;
        }

        _updatingSnippetTreeSelection = true;
        _snippetTree.SelectedNode = node;
        _updatingSnippetTreeSelection = false;
    }

    private void SelectSnippetFolderTreeNode(string folder)
    {
        var normalized = NormalizeSnippetFolder(folder);
        var node = _snippetFolderNodes.FirstOrDefault(pair => string.Equals(pair.Value, normalized, StringComparison.OrdinalIgnoreCase)).Key;
        if (node is null)
        {
            return;
        }

        _updatingSnippetTreeSelection = true;
        _snippetTree.SelectedNode = node;
        _updatingSnippetTreeSelection = false;
    }

    private static bool TryGetSnippetFromTreeContent(object? content, out SnippetItemViewModel item)
    {
        if (content is FrameworkElement { Tag: SnippetItemViewModel tagged })
        {
            item = tagged;
            return true;
        }

        item = default!;
        return false;
    }

    private async Task OpenSnippetEditorAsync(SnippetItemViewModel? existing, string folder, string name, string text)
    {
        var folderBox = SnippetEditorTextBox(folder, _runtime.Translate("SnippetFolder"));
        var nameBox = SnippetEditorTextBox(name, _runtime.Translate("SnippetName"));
        var textBox = SnippetEditorTextBox(text, _runtime.Translate("SnippetText"));
        textBox.AcceptsReturn = true;
        textBox.TextWrapping = TextWrapping.Wrap;
        textBox.MinHeight = 180;
        textBox.MaxHeight = 260;
        ScrollViewer.SetVerticalScrollBarVisibility(textBox, ScrollBarVisibility.Auto);

        var templateHelp = DescriptionText("SnippetTemplateHelp", fontSize: 12, wrapping: TextWrapping.Wrap);
        var variableSamples = SnippetVariableSamplePanel(textBox);
        var referenceButton = new HyperlinkButton
        {
            Content = _runtime.Translate("SnippetTemplateReference"),
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        referenceButton.Click += (_, _) => OpenExternalUrl(SnippetVariablesUrl);

        var content = new StackPanel
        {
            Spacing = 12,
            MinWidth = 520,
            Children =
            {
                SnippetEditorField("SnippetFolder", folderBox),
                DescriptionText("SnippetFolderHint", fontSize: 12, wrapping: TextWrapping.Wrap),
                SnippetEditorField("SnippetName", nameBox),
                SnippetEditorField("SnippetText", textBox),
                templateHelp,
                variableSamples,
                referenceButton
            }
        };

        var dialog = new ContentDialog
        {
            Title = _runtime.Translate("SnippetEditor"),
            Content = content,
            PrimaryButtonText = _runtime.Translate("Save"),
            CloseButtonText = _runtime.Translate("Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = _root.XamlRoot,
            RequestedTheme = IsDark ? ElementTheme.Dark : ElementTheme.Light
        };
        dialog.IsPrimaryButtonEnabled = CanSaveSnippet(nameBox.Text, textBox.Text);
        nameBox.TextChanged += (_, _) => dialog.IsPrimaryButtonEnabled = CanSaveSnippet(nameBox.Text, textBox.Text);
        textBox.TextChanged += (_, _) => dialog.IsPrimaryButtonEnabled = CanSaveSnippet(nameBox.Text, textBox.Text);

        var result = await ShowContentDialogAsync(dialog);
        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        var parsed = ParseSnippetNameAndFolder(folderBox.Text, nameBox.Text);
        var newName = parsed.Name;
        var newText = textBox.Text;
        if (!CanSaveSnippet(newName, newText))
        {
            return;
        }

        var newFolder = parsed.Folder;
        var overwritesDifferentSnippet = _runtime.Snippets.Find(newFolder, newName) is not null
            && (existing is null
                || !string.Equals(existing.Name, newName, StringComparison.Ordinal)
                || !string.Equals(existing.Folder, newFolder, StringComparison.Ordinal));
        if (overwritesDifferentSnippet)
        {
            var confirmed = await ConfirmDialogAsync(
                _runtime.Translate("ConfirmOverwriteSnippetTitle"),
                string.Format(_runtime.Translate("ConfirmOverwriteSnippetMessage"), new Snippet(newName, newText, newFolder).DisplayName),
                _runtime.Translate("Save"),
                _runtime.Translate("Cancel"));
            if (!confirmed)
            {
                return;
            }
        }

        if (existing is not null
            && (!string.Equals(existing.Name, newName, StringComparison.Ordinal)
                || !string.Equals(existing.Folder, newFolder, StringComparison.Ordinal)))
        {
            _runtime.RemoveSnippet(existing.Folder, existing.Name);
        }

        _runtime.UpsertSnippet(newFolder, newName, newText);
        _selectedSnippet = SnippetItemViewModel.FromSnippet(new Snippet(newName, newText, newFolder));
        UpdateSelectedSnippetText();
        RefreshItems();
    }

    private static bool CanSaveSnippet(string name, string text)
    {
        return !string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(text);
    }

    private UIElement SnippetEditorField(string labelKey, TextBox input)
    {
        var panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(LocalizedText(labelKey, fontWeight: Microsoft.UI.Text.FontWeights.SemiBold));
        panel.Children.Add(input);
        return panel;
    }

    private UIElement SnippetVariableSamplePanel(TextBox target)
    {
        string[] samples =
        [
            "{{date}}",
            "{{time}}",
            "{{datetime}}",
            "{{date:yyyy/MM/dd}}",
            "{{weekday}}",
            "{{uuid}}",
            "{{shortuuid}}",
            "{{tomorrow}}",
            "{{adddays:7}}",
            "{{adddays:-7|yyyy/MM/dd}}",
            "{{filepath}}",
            "{{filename}}",
            "{{filecount}}",
            "{{randomhex:8}}",
            "{{randomnumber:4}}",
            "{{br}}"
        ];

        var panel = new StackPanel { Spacing = 7 };
        panel.Children.Add(DescriptionText("SnippetVariableSamples", fontSize: 12, wrapping: TextWrapping.Wrap));

        foreach (var rowSamples in samples.Chunk(4))
        {
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6
            };
            foreach (var sample in rowSamples)
            {
                row.Children.Add(SnippetVariableButton(sample, target));
            }

            panel.Children.Add(row);
        }

        return panel;
    }

    private Button SnippetVariableButton(string token, TextBox target)
    {
        var button = new Button
        {
            Content = token,
            MinHeight = 30,
            Padding = new Thickness(10, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        AutomationProperties.SetName(button, token);
        ToolTipService.SetToolTip(button, token);
        button.Click += (_, _) => InsertSnippetVariable(target, token);
        return button;
    }

    private static void InsertSnippetVariable(TextBox target, string token)
    {
        var text = target.Text ?? string.Empty;
        var start = Math.Clamp(target.SelectionStart, 0, text.Length);
        var length = Math.Clamp(target.SelectionLength, 0, text.Length - start);
        target.Text = text.Remove(start, length).Insert(start, token);
        target.Focus(FocusState.Programmatic);
        target.SelectionStart = start + token.Length;
        target.SelectionLength = 0;
    }

    private TextBox SnippetEditorTextBox(string text, string placeholder)
    {
        return new TextBox
        {
            Text = text,
            PlaceholderText = placeholder,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
    }

    private static (string Folder, string Name) ParseSnippetNameAndFolder(string folder, string name)
    {
        var trimmedName = name.Trim();
        var parts = trimmedName
            .Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length <= 1)
        {
            return (NormalizeSnippetFolder(folder), trimmedName);
        }

        return (string.Join("/", parts.Take(parts.Length - 1)), parts[^1]);
    }

    private static IEnumerable<string> GetFolderAndParents(string? folder)
    {
        var normalized = NormalizeSnippetFolder(folder);
        while (!string.IsNullOrWhiteSpace(normalized))
        {
            yield return normalized;
            normalized = GetParentFolder(normalized);
        }
    }

    private static string GetParentFolder(string folder)
    {
        var normalized = NormalizeSnippetFolder(folder);
        var index = normalized.LastIndexOf('/');
        return index <= 0 ? string.Empty : normalized[..index];
    }

    private static string NormalizeSnippetFolder(string? folder)
    {
        return string.Join(
            "/",
            (folder ?? string.Empty)
                .Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private async Task<string?> PromptForHotkeyAsync()
    {
        if (_root.XamlRoot is null)
        {
            return null;
        }

        _runtime.SuspendHotkey();
        var captured = string.Empty;
        var preview = new TextBox
        {
            IsReadOnly = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            PlaceholderText = _runtime.Translate("CaptureHotkeyPrompt")
        };
        var content = new StackPanel
        {
            Spacing = 12,
            MinWidth = 420,
            Children =
            {
                new TextBlock
                {
                    Text = _runtime.Translate("CaptureHotkeyPrompt"),
                    TextWrapping = TextWrapping.Wrap
                },
                preview
            }
        };
        var dialog = new ContentDialog
        {
            Title = _runtime.Translate("CaptureHotkey"),
            Content = content,
            PrimaryButtonText = _runtime.Translate("Save"),
            CloseButtonText = _runtime.Translate("Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false,
            XamlRoot = _root.XamlRoot,
            RequestedTheme = IsDark ? ElementTheme.Dark : ElementTheme.Light
        };

        void CaptureKey(KeyRoutedEventArgs e)
        {
            e.Handled = true;
            var hotkey = BuildHotkeyFromKeyEvent(e.Key);
            if (hotkey is null)
            {
                preview.Text = _runtime.Translate("HotkeyInvalid");
                dialog.IsPrimaryButtonEnabled = false;
                captured = string.Empty;
                return;
            }

            preview.Text = hotkey;
            captured = hotkey;
            dialog.IsPrimaryButtonEnabled = true;
        }

        content.KeyDown += (_, e) => CaptureKey(e);
        preview.KeyDown += (_, e) => CaptureKey(e);
        dialog.Opened += (_, _) => preview.Focus(FocusState.Programmatic);

        try
        {
            return await ShowContentDialogAsync(dialog) == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(captured)
                ? captured
                : null;
        }
        finally
        {
            _runtime.RestoreHotkey();
        }
    }

    private static string? BuildHotkeyFromKeyEvent(VirtualKey keyCode)
    {
        var parts = new List<string>();
        if (IsKeyDown(VirtualKey.Control)) parts.Add("Ctrl");
        if (IsKeyDown(VirtualKey.Shift)) parts.Add("Shift");
        if (IsKeyDown(VirtualKey.Menu)) parts.Add("Alt");

        var key = keyCode switch
        {
            VirtualKey.Control or VirtualKey.Shift or VirtualKey.Menu or VirtualKey.LeftControl or VirtualKey.RightControl or VirtualKey.LeftShift or VirtualKey.RightShift or VirtualKey.LeftMenu or VirtualKey.RightMenu => null,
            VirtualKey.Space => "Space",
            >= VirtualKey.A and <= VirtualKey.Z => keyCode.ToString(),
            >= VirtualKey.Number0 and <= VirtualKey.Number9 => ((char)('0' + (int)keyCode - (int)VirtualKey.Number0)).ToString(),
            >= VirtualKey.F1 and <= VirtualKey.F24 => keyCode.ToString(),
            _ => null
        };

        if (key is null || !parts.Any(part => part is "Ctrl" or "Alt"))
        {
            return null;
        }

        parts.Add(key);
        return string.Join("+", parts);
    }

    private static bool IsKeyDown(VirtualKey key)
    {
        return (InputKeyboardSource.GetKeyStateForCurrentThread(key) & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;
    }

    private async Task SelectPageAsync(int index)
    {
        if (IsProtectedPageIndex(index) && !await EnsureHistoryAccessUnlockedAsync(showWindow: false))
        {
            RestoreNavigationSelection();
            return;
        }

        SelectPage(index);
    }

    private static bool IsProtectedPageIndex(int index) => index is 1 or 4;

    private void SelectPage(int index)
    {
        _selectedPageIndex = index;
        _generalPage.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
        _historyPage.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
        _historySettingsPage.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
        _advancedPage.Visibility = index == 3 ? Visibility.Visible : Visibility.Collapsed;
        _snippetPage.Visibility = index == 4 ? Visibility.Visible : Visibility.Collapsed;
        _donationPage.Visibility = index == 5 ? Visibility.Visible : Visibility.Collapsed;
        _aboutPage.Visibility = index == 6 ? Visibility.Visible : Visibility.Collapsed;

        if (index >= 0 && index < _navItems.Count && !ReferenceEquals(_navigationView.SelectedItem, _navItems[index]))
        {
            _updatingNavSelection = true;
            _navigationView.SelectedItem = _navItems[index];
            _updatingNavSelection = false;
        }
    }

    private void RestoreNavigationSelection()
    {
        if (_selectedPageIndex >= 0 && _selectedPageIndex < _navItems.Count)
        {
            _updatingNavSelection = true;
            _navigationView.SelectedItem = _navItems[_selectedPageIndex];
            _updatingNavSelection = false;
        }
    }

    private void SetSidebarCollapsed(bool collapsed)
    {
        _sidebarCollapsed = collapsed;
        _navigationView.PaneDisplayMode = collapsed
            ? NavigationViewPaneDisplayMode.LeftCompact
            : NavigationViewPaneDisplayMode.Left;
        _navigationView.IsPaneOpen = !collapsed;
        UpdateNavigationPaneFooterVisibility();
    }

    private void UpdateNavigationPaneFooterVisibility()
    {
        var hideExpandedFooter = _sidebarCollapsed || !_navigationView.IsPaneOpen;
        _hotkeyPill.Visibility = hideExpandedFooter ? Visibility.Collapsed : Visibility.Visible;
        _navigationPaneFooter.Visibility = hideExpandedFooter ? Visibility.Collapsed : Visibility.Visible;
    }

    private void UpdateSidebarForWindowWidth(double width)
    {
        if (width <= SidebarAutoCollapseWidth)
        {
            _autoSidebarApplied = true;
            SetSidebarCollapsed(true);
            return;
        }

        if (width >= SidebarAutoExpandWidth && _autoSidebarApplied)
        {
            _autoSidebarApplied = false;
            SetSidebarCollapsed(false);
        }
    }

    private void UpdateNavButtonContents()
    {
        SetNavItemContent(0, "\uE80F", "General");
        SetNavItemContent(1, "\uE81C", "History");
        SetNavItemContent(2, "\uE713", "HistorySettings");
        SetNavItemContent(3, "\uE9D9", "Advanced");
        SetNavItemContent(4, "\uE8C8", "Snippets");
        SetNavItemContent(5, "\uE8D7", "Donation");
        SetNavItemContent(6, "\uE946", "About");
    }

    private NavigationViewItem CreateNavItem(int index)
    {
        return new NavigationViewItem
        {
            Tag = index,
            UseSystemFocusVisuals = true
        };
    }

    private void SetNavItemContent(int index, string glyph, string labelKey)
    {
        if (index < 0 || index >= _navItems.Count)
        {
            return;
        }

        var label = _runtime.Translate(labelKey);
        var item = _navItems[index];
        item.Content = label;
        item.Icon = new FontIcon
        {
            Glyph = glyph,
            FontFamily = new FontFamily("Segoe Fluent Icons"),
            FontSize = 16
        };
        AutomationProperties.SetName(item, label);
        ToolTipService.SetToolTip(item, label);
    }

    private static void SetCommandButton(Button button, string glyph, string label)
    {
        button.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children =
            {
                new FontIcon
                {
                    Glyph = glyph,
                    FontFamily = new FontFamily("Segoe Fluent Icons"),
                    FontSize = 14,
                    VerticalAlignment = VerticalAlignment.Center
                },
                new TextBlock
                {
                    Text = label,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center
                }
            }
        };
        AutomationProperties.SetName(button, label);
        ToolTipService.SetToolTip(button, label);
    }

    private static void SetIconButton(Button button, string glyph, string label)
    {
        button.Content = new FontIcon
        {
            Glyph = glyph,
            FontFamily = new FontFamily("Segoe Fluent Icons"),
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTipService.SetToolTip(button, label);
    }

    private static void SetHistoryFilterToggle(ToggleButton button, string label)
    {
        button.Content = label;
        AutomationProperties.SetName(button, label);
        ToolTipService.SetToolTip(button, label);
    }

    private void ApplyTheme()
    {
        var dark = IsDark;
        _root.RequestedTheme = dark ? ElementTheme.Dark : ElementTheme.Light;
        _root.Background = Brush(dark ? "#202020" : "#F5F5F5");
        ApplyTitleBarTheme();
        ApplyWindowIcon();
        RefreshAppLogoImage();
        foreach (var card in _cards)
        {
            card.Background = CardBackground();
            card.BorderBrush = CardBorderBrush();
        }
        RefreshThemeTextBrushes();
        RefreshItems();
        UpdateMaskTestPreview();
        SelectPage(_selectedPageIndex);
    }

    private void RefreshThemeTextBrushes()
    {
        var brush = DescriptionBrush();
        foreach (var textBlock in _descriptionTextBlocks)
        {
            textBlock.Foreground = brush;
        }

        foreach (var label in _toggleStateLabels.Values)
        {
            label.Foreground = brush;
        }

        _historySearchIcon.Foreground = brush;
    }

    private bool IsDark => string.Equals(_runtime.EffectiveTheme, "dark", StringComparison.OrdinalIgnoreCase);

    private Border Card(UIElement child)
    {
        return Card(child, new Thickness(18));
    }

    private Border Card(UIElement child, Thickness padding)
    {
        var card = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Padding = padding,
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            BorderBrush = CardBorderBrush(),
            Background = CardBackground(),
            Child = child
        };
        _cards.Add(card);
        return card;
    }

    private Border DialogCard(UIElement child) => new()
    {
        Padding = new Thickness(16),
        CornerRadius = new CornerRadius(6),
        BorderThickness = new Thickness(1),
        BorderBrush = CardBorderBrush(),
        Background = CardBackground(),
        Child = child
    };

    private void UpdateSettingsPageWidth(double availableWidth)
    {
        var width = Math.Min(SettingsPageMaxWidth, availableWidth);
        foreach (var page in new[] { _generalPage, _historyPage, _historySettingsPage, _advancedPage, _snippetPage, _donationPage, _aboutPage })
        {
            page.Width = width;
        }
    }

    private UIElement SettingCard(string glyph, string titleKey, string descriptionKey, FrameworkElement control)
    {
        if (control is Control controlElement)
        {
            controlElement.MinWidth = control is ToggleSwitch ? 0 : 220;
        }

        control.HorizontalAlignment = HorizontalAlignment.Right;
        var actionControl = control is ToggleSwitch toggle ? ToggleActionHost(toggle) : control;

        var grid = new Grid { ColumnSpacing = 14, VerticalAlignment = VerticalAlignment.Center };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(IconCircle(glyph));

        var texts = new StackPanel { Spacing = 3 };
        texts.Children.Add(LocalizedText(titleKey, fontWeight: Microsoft.UI.Text.FontWeights.SemiBold));
        texts.Children.Add(DescriptionText(descriptionKey, fontSize: 12, wrapping: TextWrapping.Wrap));
        Grid.SetColumn(texts, 1);
        grid.Children.Add(texts);
        Grid.SetColumn(actionControl, 2);
        grid.Children.Add(actionControl);
        return Card(grid);
    }

    private Grid ToggleActionHost(ToggleSwitch toggle)
    {
        var status = new TextBlock
        {
            MinWidth = 34,
            TextAlignment = TextAlignment.Right,
            Foreground = DescriptionBrush(),
            VerticalAlignment = VerticalAlignment.Center
        };
        _descriptionTextBlocks.Add(status);
        _toggleStateLabels[toggle] = status;
        UpdateToggleStateLabel(toggle);

        toggle.HorizontalAlignment = HorizontalAlignment.Right;
        toggle.VerticalAlignment = VerticalAlignment.Center;
        toggle.Toggled += (_, _) => UpdateToggleStateLabel(toggle);

        var host = new Grid
        {
            ColumnSpacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        host.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        host.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        host.Children.Add(status);
        Grid.SetColumn(toggle, 1);
        host.Children.Add(toggle);
        return host;
    }

    private static void AlignSettingControl(Control control)
    {
        control.Height = SettingControlHeight;
        control.VerticalAlignment = VerticalAlignment.Center;
    }

    private void RefreshToggleStateLabels()
    {
        foreach (var toggle in _toggleStateLabels.Keys)
        {
            UpdateToggleStateLabel(toggle);
        }
    }

    private void RefreshLocalizedTextBlocks()
    {
        foreach (var (textBlock, key) in _localizedTextBlocks)
        {
            textBlock.Text = _runtime.Translate(key);
        }
    }

    private void UpdateToggleStateLabel(ToggleSwitch toggle)
    {
        if (_toggleStateLabels.TryGetValue(toggle, out var label))
        {
            label.Text = _runtime.Translate(toggle.IsOn ? "ToggleOn" : "ToggleOff");
        }
    }

    private UIElement InfoRow(string labelKey, string value, string? valueKey = null)
    {
        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(DescriptionText(labelKey, wrapping: TextWrapping.Wrap));
        var valueText = valueKey is null
            ? new TextBlock
            {
                Text = value,
                TextWrapping = TextWrapping.Wrap
            }
            : LocalizedText(valueKey, wrapping: TextWrapping.Wrap);
        Grid.SetColumn(valueText, 1);
        grid.Children.Add(valueText);
        return grid;
    }

    private UIElement InfoLinkRow(string labelKey, string text, string url)
    {
        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(DescriptionText(labelKey, wrapping: TextWrapping.Wrap));
        var link = new HyperlinkButton
        {
            Content = text,
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        };
        AutomationProperties.SetName(link, text);
        ToolTipService.SetToolTip(link, url);
        link.Click += (_, _) => OpenExternalUrl(url);
        Grid.SetColumn(link, 1);
        grid.Children.Add(link);
        return grid;
    }

    private Button ItemButton(string title, string subtitle)
    {
        return new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(12, 9, 12, 9),
            Content = new StackPanel
            {
                Spacing = 2,
                Children =
                {
                    new TextBlock { Text = title, TextTrimming = TextTrimming.CharacterEllipsis },
                    TrackDescriptionText(new TextBlock { Text = subtitle, FontSize = 12, Foreground = DescriptionBrush() })
                }
            }
        };
    }

    private UIElement HistoryListRow(HistoryItemViewModel item)
    {
        if (item.IsImage)
        {
            return HistoryImageListRow(item);
        }

        var grid = new Grid
        {
            ColumnSpacing = 16,
            Padding = new Thickness(12, 7, 10, 7)
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var texts = new StackPanel { Spacing = 3 };
        texts.Children.Add(new TextBlock
        {
            Text = item.Preview,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        texts.Children.Add(new TextBlock
        {
            Text = item.FormatSummary,
            FontSize = 12,
            Foreground = DescriptionBrush(),
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        grid.Children.Add(texts);

        var capturedAt = new TextBlock
        {
            Text = item.CapturedAtText,
            FontSize = 12,
            Foreground = DescriptionBrush(),
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Right
        };
        Grid.SetColumn(capturedAt, 1);
        grid.Children.Add(capturedAt);
        return grid;
    }

    private MenuFlyout CreateHistoryContextFlyout(HistoryItemViewModel item)
    {
        var flyout = new MenuFlyout();
        foreach (var option in _runtime.CreateHistoryContextOptions(item.Id))
        {
            flyout.Items.Add(CreateHistoryContextMenuItem(option));
        }

        if (flyout.Items.Count > 0)
        {
            flyout.Items.Add(new MenuFlyoutSeparator());
        }

        flyout.Items.Add(CreateHistoryDeleteContextMenuItem(item.Id));

        return flyout;
    }

    private MenuFlyoutItem CreateHistoryDeleteContextMenuItem(string id)
    {
        var option = new QuickMenuPasteOption(_runtime.Translate("Delete"), "\uE74D", () => { });
        var menuItem = new MenuFlyoutItem
        {
            Text = option.Text,
            Icon = CreateHistoryContextIcon(option)
        };
        menuItem.Click += async (_, _) => await ConfirmAndDeleteHistoryItemAsync(id);
        return menuItem;
    }

    private static MenuFlyoutItem CreateHistoryContextMenuItem(QuickMenuPasteOption option)
    {
        var menuItem = new MenuFlyoutItem
        {
            Text = option.Text,
            Icon = CreateHistoryContextIcon(option)
        };
        menuItem.Click += (_, _) => option.Invoke();
        return menuItem;
    }

    private static IconElement? CreateHistoryContextIcon(QuickMenuPasteOption option)
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

    private UIElement HistoryImageListRow(HistoryItemViewModel item)
    {
        var grid = new Grid
        {
            ColumnSpacing = 12,
            Padding = new Thickness(12, 7, 10, 7)
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.Children.Add(HistoryPreviewSlot(item, 84, 48));

        var label = new TextBlock
        {
            Text = _runtime.Translate("ImagePreview"),
            Foreground = DescriptionBrush(),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);

        var capturedAt = new TextBlock
        {
            Text = item.CapturedAtText,
            FontSize = 12,
            Foreground = DescriptionBrush(),
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Right
        };
        Grid.SetColumn(capturedAt, 2);
        grid.Children.Add(capturedAt);
        return grid;
    }

    private UIElement HistoryPreviewSlot(HistoryItemViewModel item, double width, double height)
    {
        var host = new HandCursorGrid
        {
            Width = width,
            Height = height,
            VerticalAlignment = VerticalAlignment.Center
        };
        var border = new Border
        {
            Width = width,
            Height = height,
            CornerRadius = new CornerRadius(4),
            Background = Brush(IsDark ? "#202020" : "#F7F7F7"),
            VerticalAlignment = VerticalAlignment.Center
        };
        host.Children.Add(border);

        if (item.HasPreviewImage && item.ThumbnailImageBytes is { Length: > 0 } thumbnailBytes)
        {
            border.BorderBrush = CardBorderBrush();
            border.BorderThickness = new Thickness(1);
            var image = new Image
            {
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            image.Loaded += async (_, _) => image.Source = await CreateBitmapImageAsync(thumbnailBytes);
            border.Child = image;
            host.Tapped += async (_, e) =>
            {
                e.Handled = true;
                await ShowHistoryImagePreviewAsync(item);
            };
            ToolTipService.SetToolTip(host, _runtime.Translate("ImagePreview"));
        }
        else
        {
            host.Opacity = 0;
            host.IsHitTestVisible = false;
        }

        return host;
    }

    private UIElement BuildBrandHeader()
    {
        var grid = new Grid { ColumnSpacing = 12, Margin = new Thickness(4, 0, 4, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(new Border
        {
            Width = 42,
            Height = 42,
            CornerRadius = new CornerRadius(9),
            Background = AccentBrush(24),
            Child = _appLogoImage
        });
        Grid.SetColumn(_titleText, 1);
        _titleText.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(_titleText);
        return grid;
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

    private static Border Pill(UIElement child) => new()
    {
        Padding = new Thickness(10, 7, 10, 8),
        CornerRadius = new CornerRadius(5),
        Background = new SolidColorBrush(ColorHelper.FromArgb(18, 128, 128, 128)),
        Child = child
    };

    private UIElement PageHeader(TextBlock header, TextBlock description)
    {
        return new StackPanel
        {
            Spacing = 5,
            Margin = new Thickness(0, 0, 0, 8),
            Children = { header, description }
        };
    }

    private UIElement SectionHeader(string key)
    {
        var text = DescriptionText(
            key,
            fontSize: 13,
            fontWeight: Microsoft.UI.Text.FontWeights.SemiBold,
            wrapping: TextWrapping.NoWrap);
        text.Margin = new Thickness(1, 6, 0, -8);
        return text;
    }

    private TextBlock DescriptionText(
        string key,
        double? fontSize = null,
        Windows.UI.Text.FontWeight? fontWeight = null,
        TextWrapping wrapping = TextWrapping.NoWrap)
    {
        return TrackDescriptionText(LocalizedText(key, fontSize, fontWeight, DescriptionBrush(), wrapping));
    }

    private TextBlock LocalizedText(
        string key,
        double? fontSize = null,
        Windows.UI.Text.FontWeight? fontWeight = null,
        Brush? foreground = null,
        TextWrapping wrapping = TextWrapping.NoWrap)
    {
        var textBlock = new TextBlock
        {
            Text = _runtime.Translate(key),
            TextWrapping = wrapping
        };
        if (fontSize is not null)
        {
            textBlock.FontSize = fontSize.Value;
        }

        if (fontWeight is not null)
        {
            textBlock.FontWeight = fontWeight.Value;
        }

        if (foreground is not null)
        {
            textBlock.Foreground = foreground;
        }

        _localizedTextBlocks.Add((textBlock, key));
        return textBlock;
    }

    private TextBlock TrackDescriptionText(TextBlock textBlock)
    {
        _descriptionTextBlocks.Add(textBlock);
        return textBlock;
    }

    private void TrackDescriptionText(params TextBlock[] textBlocks)
    {
        foreach (var textBlock in textBlocks)
        {
            _descriptionTextBlocks.Add(textBlock);
        }
    }

    private UIElement IconCircle(string glyph) => new Border
    {
        Width = 30,
        Height = 30,
        CornerRadius = new CornerRadius(15),
        Background = AccentBrush(28),
        Child = new FontIcon
        {
            Glyph = glyph,
            FontFamily = new FontFamily("Segoe Fluent Icons"),
            FontSize = 15,
            Foreground = AccentBrush(220)
        }
    };

    private void SizeWindow()
    {
        _hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
        _appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
        _appWindow.Resize(new SizeInt32(1120, 760));
        ApplyWindowIcon();
        _originalWndProc = NativeMethods.SetWindowLongPtr(
            _hwnd,
            NativeMethods.GwlWndproc,
            Marshal.GetFunctionPointerForDelegate(_windowProc));
    }

    private void ApplyWindowIcon()
    {
        var iconPath = AppAssets.GetAppIconPath(_runtime.EffectiveTheme);
        if (_appWindow is not null && File.Exists(iconPath))
        {
            _appWindow.SetIcon(iconPath);
        }
    }

    private IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == NativeMethods.WmClose && !_runtime.IsExiting)
        {
            _appWindow?.Hide();
            _hiddenToTray = true;
            return IntPtr.Zero;
        }

        return NativeMethods.CallWindowProc(_originalWndProc, hWnd, msg, wParam, lParam);
    }

    private void ApplyTitleBarTheme()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var dark = IsDark;
        var darkMode = dark ? 1 : 0;
        _ = DwmSetWindowAttribute(hwnd, 20, ref darkMode, sizeof(int));

        var captionColor = ColorRef(dark ? "#171717" : "#F7F7F7");
        var textColor = ColorRef(dark ? "#F3F3F3" : "#1F1F1F");
        _ = DwmSetWindowAttribute(hwnd, 35, ref captionColor, sizeof(int));
        _ = DwmSetWindowAttribute(hwnd, 36, ref textColor, sizeof(int));
    }

    private Brush CardBackground() => Brush(IsDark ? "#2B2B2B" : "#FFFFFF");

    private Brush CardBorderBrush() => Brush(IsDark ? "#3F3F3F" : "#E5E5E5");

    private Brush DescriptionBrush() => Brush(IsDark ? "#D0D5DD" : "#344054");

    private static SolidColorBrush AccentBrush(byte alpha) => new(ColorHelper.FromArgb(alpha, 0, 120, 212));

    private static SolidColorBrush Brush(string color)
    {
        if (color.Length == 9)
        {
            return new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(
                Convert.ToByte(color.Substring(1, 2), 16),
                Convert.ToByte(color.Substring(3, 2), 16),
                Convert.ToByte(color.Substring(5, 2), 16),
                Convert.ToByte(color.Substring(7, 2), 16)));
        }

        return new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(
            255,
            Convert.ToByte(color.Substring(1, 2), 16),
            Convert.ToByte(color.Substring(3, 2), 16),
            Convert.ToByte(color.Substring(5, 2), 16)));
    }

    private static int ColorRef(string color)
    {
        var r = Convert.ToByte(color.Substring(1, 2), 16);
        var g = Convert.ToByte(color.Substring(3, 2), 16);
        var b = Convert.ToByte(color.Substring(5, 2), 16);
        return r | (g << 8) | (b << 16);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);

    private static TextBlock Header(double size = 28) => new() { FontSize = size, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };

    private static TextBlock Description() => new() { Foreground = Brush("#344054"), TextWrapping = TextWrapping.Wrap };

    private static StackPanel SettingsPage() => new()
    {
        Spacing = 18,
        MaxWidth = SettingsPageMaxWidth,
        HorizontalAlignment = HorizontalAlignment.Left
    };

    private static ToggleSwitch CompactToggle() => new()
    {
        Width = 44,
        MinWidth = 44,
        OnContent = string.Empty,
        OffContent = string.Empty
    };

    private void RefreshAppLogoImage()
    {
        _appLogoImage.Source = File.Exists(AppAssets.GetAppImagePath(_runtime.EffectiveTheme))
            ? new BitmapImage(new Uri(AppAssets.GetAppImagePath(_runtime.EffectiveTheme)))
            : null;
    }

    private static void SetComboSelection(ComboBox comboBox, string tag, bool refreshSelectionBox = false)
    {
        foreach (ComboBoxItem item in comboBox.Items)
        {
            if (Equals(item.Tag, tag))
            {
                if (refreshSelectionBox && ReferenceEquals(comboBox.SelectedItem, item))
                {
                    comboBox.SelectedItem = null;
                }

                comboBox.SelectedItem = item;
                return;
            }
        }
    }

    private static void SetComboBoxText(ComboBox comboBox, string tag, string content)
    {
        foreach (ComboBoxItem item in comboBox.Items)
        {
            if (Equals(item.Tag, tag))
            {
                item.Content = content;
                return;
            }
        }
    }

    private void EnsureHotkeyComboItem(string hotkey)
    {
        foreach (ComboBoxItem item in _hotkeyBox.Items)
        {
            if (Equals(item.Tag, hotkey))
            {
                item.Content = hotkey;
                return;
            }
        }

        _hotkeyBox.Items.Add(new ComboBoxItem { Content = hotkey, Tag = hotkey });
    }

    private void EnsureHistoryLimitComboItem(int count)
    {
        var tag = count.ToString();
        foreach (ComboBoxItem item in _maxHistoryItemsBox.Items)
        {
            if (Equals(item.Tag, tag))
            {
                item.Content = tag;
                return;
            }
        }

        _maxHistoryItemsBox.Items.Add(new ComboBoxItem { Content = tag, Tag = tag });
    }

    private static string CreateSnippetName(string text)
    {
        var normalized = string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= 32 ? normalized : normalized[..32];
    }
}

public sealed record HistoryItemViewModel(
    string Id,
    string Preview,
    string FormatSummary,
    DateTimeOffset CapturedAt,
    bool IsImage = false,
    byte[]? ThumbnailImageBytes = null)
{
    public string CapturedAtText => CapturedAt.LocalDateTime.ToString("yyyy/MM/dd HH:mm");

    public bool HasPreviewImage => ThumbnailImageBytes is { Length: > 0 };

    public override string ToString() => $"{Preview}  {FormatSummary}  {CapturedAtText}";
}

public sealed record SnippetItemViewModel(string Folder, string Name, string DisplayName)
{
    public static SnippetItemViewModel FromSnippet(Snippet snippet)
    {
        return new SnippetItemViewModel(snippet.Folder, snippet.Name, snippet.DisplayName);
    }

    public override string ToString() => DisplayName;
}

public sealed class HandCursorGrid : Grid
{
    public HandCursorGrid()
    {
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
    }
}
