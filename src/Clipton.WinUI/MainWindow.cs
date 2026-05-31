using Microsoft.UI.Xaml.Automation;
using Clipton.Core;
using Microsoft.UI;
using Microsoft.UI.Input;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI.Core;
using WinRT.Interop;
using Forms = System.Windows.Forms;

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
    private const string SnippetVariablesUrl = "https://mmiyaji.github.io/clipton/snippet-variables/";
    private readonly CliptonRuntime _runtime;
    private readonly Grid _root = new();
    private readonly NavigationView _navigationView = new();
    private readonly ScrollViewer _contentScroller = new();
    private readonly StackPanel _generalPage = SettingsPage();
    private readonly StackPanel _historyPage = SettingsPage();
    private readonly StackPanel _historySettingsPage = SettingsPage();
    private readonly StackPanel _snippetPage = SettingsPage();
    private readonly StackPanel _aboutPage = SettingsPage();
    private StackPanel? _historyListHost;
    private ListView? _historyListView;
    private TextBlock? _historyEmptyText;
    private TreeView? _snippetTree;
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
    private readonly StackPanel _navigationPaneFooter = new() { Padding = new Thickness(8, 10, 8, 0), Spacing = 12 };
    private readonly Border _hotkeyPill;
    private readonly TextBlock _generalHeaderText = Header();
    private readonly TextBlock _generalDescriptionText = Description();
    private readonly TextBlock _historyHeaderText = Header();
    private readonly TextBlock _historyDescriptionText = Description();
    private readonly TextBlock _historySettingsHeaderText = Header();
    private readonly TextBlock _historySettingsDescriptionText = Description();
    private readonly TextBlock _snippetHeaderText = Header();
    private readonly TextBlock _snippetDescriptionText = Description();
    private readonly TextBlock _snippetFormTitle = Header(18);
    private readonly TextBlock _aboutHeaderText = Header();
    private readonly TextBlock _aboutDescriptionText = Description();
    private readonly ComboBox _hotkeyBox = new();
    private readonly Button _captureHotkeyButton = new();
    private readonly Button _resetHotkeyButton = new();
    private readonly ComboBox _themeBox = new();
    private readonly ComboBox _localeBox = new();
    private readonly ComboBox _quickMenuImagePreviewSizeBox = new();
    private readonly ComboBox _quickMenuSearchShortcutBox = new();
    private readonly ComboBox _quickMenuPlainTextShortcutBox = new();
    private readonly ComboBox _quickMenuMaskShortcutBox = new();
    private readonly ComboBox _quickMenuCapturedAtShortcutBox = new();
    private readonly ToggleSwitch _quickMenuShowCapturedAtToggle = CompactToggle();
    private readonly ToggleSwitch _quickMenuShowShortcutHintsToggle = CompactToggle();
    private readonly ToggleSwitch _startupToggle = CompactToggle();
    private readonly ToggleSwitch _hideSettingsWindowOnStartupToggle = CompactToggle();
    private readonly ToggleSwitch _pauseCaptureToggle = CompactToggle();
    private readonly ToggleSwitch _diagnosticLoggingToggle = CompactToggle();
    private readonly ToggleSwitch _persistHistoryToggle = CompactToggle();
    private readonly ToggleSwitch _maskSensitiveContentToggle = CompactToggle();
    private readonly Button _maskDefinitionsButton = new();
    private readonly StackPanel _maskDefinitionsPanel = new() { Spacing = 12 };
    private readonly ComboBox _maskPrefixBox = new();
    private readonly TextBox _maskPatternsBox = new();
    private readonly TextBox _maskTestBox = new();
    private readonly TextBlock _maskDefinitionsErrorText = Description();
    private readonly TextBlock _maskTestResultText = Description();
    private readonly ComboBox _maxHistoryItemsBox = new();
    private readonly ComboBox _clipboardCaptureDelayBox = new();
    private readonly ToggleSwitch _folderModeToggle = CompactToggle();
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
    private TextBlock? _selectedSnippetText;
    private readonly Button _newSnippetButton = new();
    private readonly Button _exportSnippetsButton = new();
    private readonly Button _importSnippetsButton = new();
    private readonly Button _saveSnippetButton = new();
    private readonly Button _pasteSnippetButton = new();
    private readonly Button _deleteSnippetButton = new();
    private readonly Button _termsButton = new();
    private readonly Button _privacyButton = new();
    private readonly Button _exitApplicationButton = new();
    private readonly Button _openLogsButton = new();
    private readonly Button _clearLogsButton = new();
    private string? _selectedHistoryId;
    private SnippetItemViewModel? _selectedSnippet;
    private string _selectedSnippetFolder = string.Empty;
    private bool _updatingSnippetTreeSelection;
    private string _historySearchQuery = string.Empty;
    private int _historyVisibleLimit = HistoryDisplayBatchSize;
    private bool _loading;
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
    private bool _generalPageBuilt;
    private bool _historyPageBuilt;
    private bool _historySettingsPageBuilt;
    private bool _snippetPageBuilt;
    private bool _aboutPageBuilt;
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
            _snippetDescriptionText,
            _aboutDescriptionText,
            _historySearchStatusText,
            _maskDefinitionsErrorText,
            _maskTestResultText);
        Title = "Clipton";
        BuildUi();
        ApplyTheme();
        SizeWindow();
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

    public void ShowHistoryPage()
    {
        SelectPage(1);
        ShowSettingsWindow();
    }

    public void ShowOnboardingIfNeeded()
    {
        if (_runtime.Settings.InitialLaunchCompleted || _onboardingDialogOpen)
        {
            return;
        }

        _onboardingDialogOpen = true;
        _ = DispatcherQueue.TryEnqueue(async () =>
        {
            await Task.Delay(250);
            await ShowOnboardingDialogAsync();
            _onboardingDialogOpen = false;
        });
    }

    public void RefreshItems()
    {
        if (_historyPageBuilt)
        {
            RefreshHistoryItems();
        }

        if (_snippetPageBuilt)
        {
            RefreshSnippetTree();
        }
    }

    private StackPanel HistoryListHost => _historyListHost ??= new StackPanel { Spacing = 10 };

    private ListView HistoryListView => _historyListView ??= new ListView();

    private TextBlock HistoryEmptyText => _historyEmptyText ??= TrackDescriptionText(Description());

    private TreeView SnippetTree => _snippetTree ??= new TreeView();

    private TextBlock SelectedSnippetText => _selectedSnippetText ??= TrackDescriptionText(Description());

    private void RefreshHistoryItems()
    {
        var historyListView = HistoryListView;
        var historyEmptyText = HistoryEmptyText;
        var loadMoreHistoryButton = _loadMoreHistoryButton;

        historyListView.Items.Clear();
        var historyItems = _runtime.History.Items.Where(HistoryMatchesSearch).ToArray();
        var visibleHistoryItems = historyItems.Take(_historyVisibleLimit).ToArray();
        foreach (var snapshot in visibleHistoryItems)
        {
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
                historyListView.SelectedItem = listItem;
            };
            historyListView.Items.Add(listItem);
        }

        historyEmptyText.Text = string.IsNullOrWhiteSpace(_historySearchQuery) ? _runtime.Translate("HistoryEmpty") : _runtime.Translate("NoSearchResults");
        historyEmptyText.Visibility = visibleHistoryItems.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        loadMoreHistoryButton.Visibility = historyItems.Length > visibleHistoryItems.Length ? Visibility.Visible : Visibility.Collapsed;
        if (visibleHistoryItems.Length == 0)
        {
            _selectedHistoryId = null;
        }
        else if (historyItems.Length > visibleHistoryItems.Length)
        {
            loadMoreHistoryButton.Content = string.Format(_runtime.Translate("LoadMoreHistory"), historyItems.Length - visibleHistoryItems.Length);
        }

        if (_selectedHistoryId is not null)
        {
            foreach (ListViewItem item in historyListView.Items)
            {
                if (item.Tag is HistoryItemViewModel historyItem && string.Equals(historyItem.Id, _selectedHistoryId, StringComparison.Ordinal))
                {
                    historyListView.SelectedItem = item;
                    break;
                }
            }
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
        _snippetHeaderText.Text = t("Snippets");
        _snippetDescriptionText.Text = t("SnippetDescription");
        _snippetFormTitle.Text = t("SnippetEditor");
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
        if (_historyPageBuilt)
        {
            _loadMoreHistoryButton.Content = string.Format(t("LoadMoreHistory"), 0);
        }
        SetCommandButton(_newSnippetButton, "\uE710", t("NewSnippet"));
        SetCommandButton(_exportSnippetsButton, "\uEDE1", t("Export"));
        SetCommandButton(_importSnippetsButton, "\uE896", t("Import"));
        SetCommandButton(_saveSnippetButton, "\uE70F", t("EditSnippet"));
        SetCommandButton(_pasteSnippetButton, "\uE77F", t("Paste"));
        SetCommandButton(_deleteSnippetButton, "\uE74D", t("Delete"));
        _termsButton.Content = t("TermsOfUse");
        _privacyButton.Content = t("PrivacyPolicy");
        SetCommandButton(_exitApplicationButton, "\uE8BB", t("ExitApplication"));
        SetCommandButton(_openLogsButton, "\uE838", t("OpenLogs"));
        SetCommandButton(_clearLogsButton, "\uE74D", t("ClearLogs"));
        _captureHotkeyButton.Content = t("CaptureHotkey");
        _resetHotkeyButton.Content = t("ResetHotkey");
        SetComboBoxText(_themeBox, "system", t("ThemeSystem"));
        SetComboBoxText(_themeBox, "light", t("ThemeLight"));
        SetComboBoxText(_themeBox, "dark", t("ThemeDark"));
        SetComboBoxText(_localeBox, "system", t("LanguageSystem"));
        SetComboBoxText(_localeBox, "en", t("LanguageEnglish"));
        SetComboBoxText(_localeBox, "ja", t("LanguageJapanese"));
        SetComboBoxText(_quickMenuImagePreviewSizeBox, "none", t("ImagePreviewSizeNone"));
        SetComboBoxText(_quickMenuImagePreviewSizeBox, "small", t("ImagePreviewSizeSmall"));
        SetComboBoxText(_quickMenuImagePreviewSizeBox, "medium", t("ImagePreviewSizeMedium"));
        SetComboBoxText(_quickMenuImagePreviewSizeBox, "large", t("ImagePreviewSizeLarge"));
        SetComboBoxText(_clipboardCaptureDelayBox, "0", t("ClipboardCaptureDelayImmediate"));
        SetComboBoxText(_clipboardCaptureDelayBox, "50", string.Format(t("Milliseconds"), 50));
        SetComboBoxText(_clipboardCaptureDelayBox, "100", string.Format(t("Milliseconds"), 100));
        SetComboBoxText(_clipboardCaptureDelayBox, "150", string.Format(t("Milliseconds"), 150));
        SetComboBoxText(_clipboardCaptureDelayBox, "250", string.Format(t("Milliseconds"), 250));
        SetComboBoxText(_clipboardCaptureDelayBox, "500", string.Format(t("Milliseconds"), 500));
        SetComboBoxText(_clipboardCaptureDelayBox, "1000", string.Format(t("Milliseconds"), 1000));
        _startupToggle.IsOn = _runtime.Settings.StartWithWindows;
        _hideSettingsWindowOnStartupToggle.IsOn = _runtime.Settings.HideSettingsWindowOnStartup;
        _pauseCaptureToggle.IsOn = _runtime.Settings.PauseCapture;
        _diagnosticLoggingToggle.IsOn = _runtime.Settings.DiagnosticLoggingEnabled;
        _persistHistoryToggle.IsOn = _runtime.Settings.PersistEncryptedHistory;
        _maskSensitiveContentToggle.IsOn = _runtime.Settings.MaskSensitiveContent;
        EnsureHistoryLimitComboItem(_runtime.Settings.MaxHistoryItems);
        SetComboSelection(_maxHistoryItemsBox, _runtime.Settings.MaxHistoryItems.ToString());
        SetComboSelection(_clipboardCaptureDelayBox, _runtime.Settings.ClipboardCaptureDelayMilliseconds.ToString());
        _folderModeToggle.IsOn = _runtime.Settings.FolderMode;
        _quickMenuShowCapturedAtToggle.IsOn = _runtime.Settings.QuickMenuShowCapturedAt;
        _quickMenuShowShortcutHintsToggle.IsOn = _runtime.Settings.QuickMenuShowShortcutHints;
        RefreshToggleStateLabels();
        RefreshLocalizedTextBlocks();
        EnsureHotkeyComboItem(_runtime.Settings.Hotkey);
        SetComboSelection(_hotkeyBox, _runtime.Settings.Hotkey);
        SetComboSelection(_themeBox, _runtime.Settings.Theme);
        SetComboSelection(_localeBox, _runtime.Settings.Locale);
        SetComboSelection(_quickMenuImagePreviewSizeBox, _runtime.Settings.QuickMenuImagePreviewSize);
        SetComboSelection(_quickMenuSearchShortcutBox, _runtime.Settings.QuickMenuShortcuts.Search);
        SetComboSelection(_quickMenuPlainTextShortcutBox, _runtime.Settings.QuickMenuShortcuts.PastePlainText);
        SetComboSelection(_quickMenuMaskShortcutBox, _runtime.Settings.QuickMenuShortcuts.ToggleMaskReveal);
        SetComboSelection(_quickMenuCapturedAtShortcutBox, _runtime.Settings.QuickMenuShortcuts.ToggleCapturedAt);
        if (_snippetPageBuilt)
        {
            UpdateSelectedSnippetText();
        }
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
        _navigationView.SelectionChanged += (_, args) =>
        {
            if (_updatingNavSelection || args.SelectedItem is not NavigationViewItem item || item.Tag is not int index)
            {
                return;
            }

            SelectPage(index);
        };
        _navigationView.PaneOpened += (_, _) =>
        {
            _sidebarCollapsed = false;
            _navigationPaneFooter.Visibility = Visibility.Visible;
        };
        _navigationView.PaneClosed += (_, _) =>
        {
            _sidebarCollapsed = true;
            _navigationPaneFooter.Visibility = Visibility.Collapsed;
        };
        _navigationPaneFooter.Children.Add(_hotkeyPill);
        _hotkeyText.Margin = new Thickness(8, 0, 8, 4);
        _navigationPaneFooter.Children.Add(_brandHeader);
        _navigationView.PaneFooter = _navigationPaneFooter;
        foreach (var index in Enumerable.Range(0, 5))
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
        contentHost.Children.Add(_snippetPage);
        contentHost.Children.Add(_aboutPage);
        _navigationView.Content = _contentScroller;
        _root.Children.Add(_navigationView);

        SelectPage(0);
    }

    private void BuildGeneralPage()
    {
        if (_generalPageBuilt)
        {
            return;
        }

        _generalPageBuilt = true;
        _generalPage.Children.Add(PageHeader(_generalHeaderText, _generalDescriptionText));
        _generalPage.Children.Add(SectionHeader("ActivationSection"));
        foreach (var hotkey in new[] { "Ctrl+Shift+V", "Ctrl+Alt+V", "Alt+Space" })
        {
            _hotkeyBox.Items.Add(new ComboBoxItem { Content = hotkey, Tag = hotkey });
        }

        _hotkeyBox.SelectionChanged += async (_, _) =>
        {
            if (_loading || (_hotkeyBox.SelectedItem as ComboBoxItem)?.Tag is not string hotkey) return;
            await TryApplyHotkeyAsync(hotkey);
        };
        _captureHotkeyButton.Click += async (_, _) => await CaptureCustomHotkeyAsync();
        _resetHotkeyButton.Click += async (_, _) => await TryApplyHotkeyAsync(HotkeyGesture.Default.ToString());
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

        _startupToggle.Toggled += async (_, _) => await SetStartupAsync();
        _generalPage.Children.Add(SettingCard("\uE7C3", "Startup", "StartupDescription", _startupToggle));
        _hideSettingsWindowOnStartupToggle.Toggled += (_, _) => SaveStartupWindowOptions();
        _generalPage.Children.Add(SettingCard("\uE8BB", "HideSettingsWindowOnStartup", "HideSettingsWindowOnStartupDescription", _hideSettingsWindowOnStartupToggle));

        _generalPage.Children.Add(SectionHeader("QuickMenuSection"));
        _folderModeToggle.Toggled += (_, _) => SaveHistoryOptions();
        _generalPage.Children.Add(SettingCard("\uE8B7", "FolderMode", "FolderModeDescription", _folderModeToggle));

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
            ["M", "Ctrl+M"]);
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
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
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
        if (_historyPageBuilt)
        {
            return;
        }

        _historyPageBuilt = true;
        _historyPage.Children.Add(PageHeader(_historyHeaderText, _historyDescriptionText));
        _historyPage.Children.Add(SectionHeader("HistorySection"));
        _historyPage.Children.Add(BuildHistorySearchPanel());
        var historyListView = HistoryListView;
        var historyEmptyText = HistoryEmptyText;
        var historyListHost = HistoryListHost;
        historyListView.SelectionMode = ListViewSelectionMode.Single;
        historyListView.HorizontalAlignment = HorizontalAlignment.Stretch;
        historyListView.SelectionChanged += (_, _) =>
        {
            if (historyListView.SelectedItem is ListViewItem { Tag: HistoryItemViewModel item })
            {
                _selectedHistoryId = item.Id;
            }
        };
        historyListView.DoubleTapped += (_, _) =>
        {
            if (historyListView.SelectedItem is ListViewItem { Tag: HistoryItemViewModel item })
            {
                _runtime.PasteHistoryItem(item.Id, asPlainText: false);
            }
        };
        historyEmptyText.Margin = new Thickness(4, 6, 4, 6);
        _loadMoreHistoryButton.HorizontalAlignment = HorizontalAlignment.Left;
        historyListHost.Children.Add(historyListView);
        historyListHost.Children.Add(historyEmptyText);
        historyListHost.Children.Add(_loadMoreHistoryButton);

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
        _historyPage.Children.Add(Card(historyListHost, new Thickness(8, 8, 8, 10)));
    }

    private void BuildHistorySettingsPage()
    {
        if (_historySettingsPageBuilt)
        {
            return;
        }

        _historySettingsPageBuilt = true;
        _historySettingsPage.Children.Add(PageHeader(_historySettingsHeaderText, _historySettingsDescriptionText));
        _historySettingsPage.Children.Add(SectionHeader("CapturePrivacySection"));
        foreach (var toggle in new[] { _pauseCaptureToggle, _persistHistoryToggle, _maskSensitiveContentToggle })
        {
            toggle.Toggled += (_, _) => SaveHistoryOptions();
        }

        _historySettingsPage.Children.Add(SettingCard("\uE769", "PauseCapture", "PauseCaptureDescription", _pauseCaptureToggle));
        _diagnosticLoggingToggle.Toggled += (_, _) => SaveDiagnosticLogging();
        var diagnosticControls = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        _openLogsButton.Click += (_, _) => _runtime.OpenDiagnosticLogDirectory();
        _clearLogsButton.Click += (_, _) => _runtime.ClearDiagnosticLogs();
        diagnosticControls.Children.Add(_openLogsButton);
        diagnosticControls.Children.Add(_clearLogsButton);
        diagnosticControls.Children.Add(ToggleActionHost(_diagnosticLoggingToggle));
        _historySettingsPage.Children.Add(SettingCard("\uE946", "DiagnosticLogging", "DiagnosticLoggingDescription", diagnosticControls));
        _historySettingsPage.Children.Add(SettingCard("\uE72E", "PersistHistory", "PersistHistoryDescription", _persistHistoryToggle));
        _maxHistoryItemsBox.Width = 180;
        foreach (var count in new[] { 50, 100, 200, 500, 1000 })
        {
            _maxHistoryItemsBox.Items.Add(new ComboBoxItem { Tag = count.ToString(), Content = count.ToString() });
        }

        _maxHistoryItemsBox.SelectionChanged += (_, _) => ChangeMaxHistoryItems();
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
        var saveButton = new Button
        {
            Content = _runtime.Translate("Save"),
            MinWidth = 96,
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 2, 0, 0)
        };
        AutomationProperties.SetName(saveButton, _runtime.Translate("Save"));
        saveButton.Click += (_, _) => SaveMaskDefinitions();

        _maskDefinitionsPanel.Children.Add(prefixRow);
        _maskDefinitionsPanel.Children.Add(LocalizedText("MaskPatternDefinitions", fontWeight: Microsoft.UI.Text.FontWeights.SemiBold));
        _maskDefinitionsPanel.Children.Add(DescriptionText("MaskDefinitionsDescription", fontSize: 12, wrapping: TextWrapping.Wrap));
        _maskDefinitionsPanel.Children.Add(_maskPatternsBox);
        _maskDefinitionsPanel.Children.Add(LocalizedText("MaskTestText", fontWeight: Microsoft.UI.Text.FontWeights.SemiBold));
        _maskDefinitionsPanel.Children.Add(DescriptionText("MaskTestDescription", fontSize: 12, wrapping: TextWrapping.Wrap));
        _maskDefinitionsPanel.Children.Add(_maskTestBox);
        _maskDefinitionsPanel.Children.Add(_maskTestResultText);
        _maskDefinitionsPanel.Children.Add(_maskDefinitionsErrorText);
        _maskDefinitionsPanel.Children.Add(saveButton);
        _maskDefinitionsCard = Card(_maskDefinitionsPanel);
        _maskDefinitionsCard.Visibility = Visibility.Collapsed;
        return _maskDefinitionsCard;
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

            FrameworkElement target = _maskPatternsBox.ActualHeight > 0 ? _maskPatternsBox : _maskDefinitionsCard;
            var point = target.TransformToVisual(_contentScroller).TransformPoint(new Windows.Foundation.Point(0, 0));
            var targetOffset = Math.Max(0, _contentScroller.VerticalOffset + point.Y - 28);
            _contentScroller.ChangeView(null, targetOffset, null, true);
        });

        await Task.Delay(180);
        DispatcherQueue.TryEnqueue(() =>
        {
            _maskPatternsBox.Focus(FocusState.Programmatic);
        });
    }

    private void SaveMaskDefinitions()
    {
        var patterns = GetCurrentMaskPatterns();
        var invalidPatterns = SensitiveContentDetector.GetInvalidCustomPatterns(patterns);
        if (invalidPatterns.Length > 0)
        {
            _maskDefinitionsErrorText.Text = string.Format(_runtime.Translate("MaskDefinitionInvalid"), invalidPatterns[0]);
            _maskDefinitionsErrorText.Visibility = Visibility.Visible;
            return;
        }

        _maskDefinitionsErrorText.Visibility = Visibility.Collapsed;
        _runtime.SetMaskDefinitionOptions(GetSelectedMaskPrefixLength(), patterns);
        RefreshItems();
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

        var patterns = GetCurrentMaskPatterns();
        var invalidPatterns = SensitiveContentDetector.GetInvalidCustomPatterns(patterns);
        if (invalidPatterns.Length > 0)
        {
            _maskTestResultText.Text = string.Format(_runtime.Translate("MaskDefinitionInvalid"), invalidPatterns[0]);
            _maskTestResultText.Foreground = Brush(IsDark ? "#FFB4AB" : "#B42318");
            return;
        }

        try
        {
            var (preview, count) = CreateCustomMaskTestPreview(testText, patterns, GetSelectedMaskPrefixLength());
            var result = count > 0
                ? string.Format(_runtime.Translate("MaskTestResult"), count, preview)
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

    private static (string Preview, int MatchCount) CreateCustomMaskTestPreview(
        string text,
        IEnumerable<string> patterns,
        int visiblePrefixLength)
    {
        const int maskGlyphCount = 8;
        var result = text;
        var count = 0;
        var timeout = TimeSpan.FromMilliseconds(120);
        foreach (var pattern in SensitiveContentDetector.ValidateCustomPatterns(patterns))
        {
            result = Regex.Replace(
                result,
                pattern,
                match =>
                {
                    count++;
                    var visible = match.Value[..Math.Min(Math.Max(visiblePrefixLength, 0), match.Value.Length)];
                    return $"{visible}{new string('\u2022', maskGlyphCount)}";
                },
                RegexOptions.IgnoreCase,
                timeout);
        }

        return (result, count);
    }

    private bool HistoryMatchesSearch(ClipboardSnapshot item)
    {
        var filter = SearchFilter.Parse(_historySearchQuery);
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
            var preview = _runtime.CreateHistoryItemViewModel(item).Preview;
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
        RefreshItems();
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
        RefreshItems();
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
        if (_snippetPageBuilt)
        {
            return;
        }

        _snippetPageBuilt = true;
        _snippetPage.Children.Add(PageHeader(_snippetHeaderText, _snippetDescriptionText));
        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var snippetTree = SnippetTree;
        snippetTree.SelectionMode = TreeViewSelectionMode.Single;
        snippetTree.MinHeight = 180;
        snippetTree.HorizontalAlignment = HorizontalAlignment.Stretch;
        snippetTree.ItemTemplate = (DataTemplate)XamlReader.Load("""
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                <ContentPresenter Content="{Binding Content}" />
            </DataTemplate>
            """);
        snippetTree.ItemInvoked += (_, e) =>
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
        snippetTree.SelectionChanged += (_, _) =>
        {
            if (_updatingSnippetTreeSelection)
            {
                return;
            }

            if (snippetTree.SelectedNode is { } node && _snippetNodes.TryGetValue(node, out var item))
            {
                SetSelectedSnippet(item);
            }
            else if (snippetTree.SelectedNode is { } folderNode && _snippetFolderNodes.TryGetValue(folderNode, out var folder))
            {
                SetSelectedSnippetFolder(folder);
            }
        };
        grid.Children.Add(Card(snippetTree));

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
        details.Children.Add(SelectedSnippetText);
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
        _deleteSnippetButton.Click += (_, _) =>
        {
            if (_selectedSnippet is null) return;
            _runtime.RemoveSnippet(_selectedSnippet.Folder, _selectedSnippet.Name);
            _selectedSnippet = null;
            UpdateSelectedSnippetText();
            RefreshItems();
        };
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
        if (_aboutPageBuilt)
        {
            return;
        }

        _aboutPageBuilt = true;
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
        documents.Children.Add(LocalizedText("LegalDescription",
            fontSize: 14,
            foreground: DescriptionBrush(),
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

    private static void OpenExternalUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            Forms.Clipboard.SetText(url);
        }
    }

    private async Task SetStartupAsync()
    {
        if (_loading) return;
        await _runtime.SetStartWithWindowsAsync(_startupToggle.IsOn);
        RefreshTexts();
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

    private async Task CaptureCustomHotkeyAsync()
    {
        var hotkey = await PromptForHotkeyAsync();
        if (hotkey is not null)
        {
            await TryApplyHotkeyAsync(hotkey);
        }
    }

    private void SaveHistoryOptions()
    {
        if (_loading) return;
        _runtime.SetPauseCapture(_pauseCaptureToggle.IsOn);
        _runtime.SetPersistEncryptedHistory(_persistHistoryToggle.IsOn);
        _runtime.SetMaskSensitiveContent(_maskSensitiveContentToggle.IsOn);
        _runtime.SetFolderMode(_folderModeToggle.IsOn);
        RefreshItems();
    }

    private void SaveDiagnosticLogging()
    {
        if (_loading) return;
        _runtime.SetDiagnosticLogging(_diagnosticLoggingToggle.IsOn);
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

    private void ChangeMaxHistoryItems()
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

    private void SaveQuickMenuDisplayOptions()
    {
        if (_loading) return;
        _runtime.SetQuickMenuShowCapturedAt(_quickMenuShowCapturedAtToggle.IsOn);
        _runtime.SetQuickMenuShowShortcutHints(_quickMenuShowShortcutHintsToggle.IsOn);
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
        SelectPage(3);
        _ = OpenSnippetEditorAsync(null, "History", CreateSnippetName(item.Text), item.Text);
    }

    private async Task ExportHistoryAsync()
    {
        var path = await SelectExportPathAsync($"clipton-history-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        if (path is null)
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

        await RunImportExportActionAsync(
            "ExportSnippets",
            () => string.Format(_runtime.Translate("ExportComplete"), _runtime.ExportSnippets(path)));
    }

    private async Task ImportSnippetsAsync()
    {
        var path = await SelectImportPathAsync();
        if (path is null)
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
        await dialog.ShowAsync();
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

        var result = await dialog.ShowAsync();
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
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private async Task ShowHistoryImagePreviewAsync(HistoryItemViewModel item)
    {
        if (_root.XamlRoot is null
            || item.PreviewImagePath is not { } path
            || !File.Exists(path))
        {
            return;
        }

        var image = new Image
        {
            Source = new BitmapImage(new Uri(path)),
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

        await dialog.ShowAsync();
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
        SelectedSnippetText.Text = _selectedSnippet is null
            ? string.IsNullOrWhiteSpace(_selectedSnippetFolder)
                ? _runtime.Translate("SnippetEditorEmpty")
                : string.Format(_runtime.Translate("SnippetFolderSelected"), _selectedSnippetFolder)
            : $"{_selectedSnippet.DisplayName}\n{_selectedSnippet.Folder}";
    }

    private void RefreshSnippetTree()
    {
        _updatingSnippetTreeSelection = true;
        var snippetTree = SnippetTree;
        _snippetNodes.Clear();
        _snippetFolderNodes.Clear();
        snippetTree.RootNodes.Clear();

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
                snippetTree.RootNodes.Add(snippetNode);
            }
        }

        if (snippetTree.RootNodes.Count == 0)
        {
            snippetTree.RootNodes.Add(new TreeViewNode
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
            SnippetTree.RootNodes.Add(node);
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
        SnippetTree.SelectedNode = node;
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
        SnippetTree.SelectedNode = node;
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

        var result = await dialog.ShowAsync();
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
            return await dialog.ShowAsync() == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(captured)
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

    private void SelectPage(int index)
    {
        EnsurePageBuilt(index);
        _selectedPageIndex = index;
        _generalPage.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
        _historyPage.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
        _historySettingsPage.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
        _snippetPage.Visibility = index == 3 ? Visibility.Visible : Visibility.Collapsed;
        _aboutPage.Visibility = index == 4 ? Visibility.Visible : Visibility.Collapsed;

        if (index >= 0 && index < _navItems.Count && !ReferenceEquals(_navigationView.SelectedItem, _navItems[index]))
        {
            _updatingNavSelection = true;
            _navigationView.SelectedItem = _navItems[index];
            _updatingNavSelection = false;
        }

        RefreshTexts();
        if (index == 1)
        {
            RefreshHistoryItems();
        }
        else if (index == 3)
        {
            RefreshSnippetTree();
        }
    }

    private void EnsurePageBuilt(int index)
    {
        switch (index)
        {
            case 0:
                BuildGeneralPage();
                break;
            case 1:
                BuildHistoryPage();
                break;
            case 2:
                BuildHistorySettingsPage();
                break;
            case 3:
                BuildSnippetPage();
                break;
            case 4:
                BuildAboutPage();
                break;
        }
    }

    private void SetSidebarCollapsed(bool collapsed)
    {
        _sidebarCollapsed = collapsed;
        _navigationView.PaneDisplayMode = collapsed
            ? NavigationViewPaneDisplayMode.LeftCompact
            : NavigationViewPaneDisplayMode.Left;
        _navigationView.IsPaneOpen = !collapsed;
        _navigationPaneFooter.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
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
        SetNavItemContent(3, "\uE8C8", "Snippets");
        SetNavItemContent(4, "\uE946", "About");
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
        foreach (var card in _cards)
        {
            card.Background = CardBackground();
            card.BorderBrush = CardBorderBrush();
        }
        RefreshThemeTextBrushes();
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
        foreach (var page in new[] { _generalPage, _historyPage, _historySettingsPage, _snippetPage, _aboutPage })
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

        flyout.Items.Add(CreateHistoryContextMenuItem(new QuickMenuPasteOption(
            _runtime.Translate("Delete"),
            "\uE74D",
            () =>
            {
                _runtime.RemoveHistoryItem(item.Id);
                if (string.Equals(_selectedHistoryId, item.Id, StringComparison.Ordinal))
                {
                    _selectedHistoryId = null;
                }
            })));

        return flyout;
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

        if (item.HasPreviewImage && item.ThumbnailImagePath is { } path && File.Exists(path))
        {
            border.BorderBrush = CardBorderBrush();
            border.BorderThickness = new Thickness(1);
            border.Child = new Image
            {
                Source = new BitmapImage(new Uri(path)),
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
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
            Child = CreateAppLogoImage(32)
        });
        Grid.SetColumn(_titleText, 1);
        _titleText.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(_titleText);
        return grid;
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

    private Brush DescriptionBrush() => Brush(IsDark ? "#C7C7C7" : "#667085");

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

    private static TextBlock Description() => new() { Foreground = Brush("#667085"), TextWrapping = TextWrapping.Wrap };

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

    private Image CreateAppLogoImage(double size) => new()
    {
        Width = size,
        Height = size,
        Source = File.Exists(AppAssets.GetAppImagePath(_runtime.EffectiveTheme))
            ? new BitmapImage(new Uri(AppAssets.GetAppImagePath(_runtime.EffectiveTheme)))
            : null
    };

    private static void SetComboSelection(ComboBox comboBox, string tag)
    {
        foreach (ComboBoxItem item in comboBox.Items)
        {
            if (Equals(item.Tag, tag))
            {
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
    string? ThumbnailImagePath = null,
    string? PreviewImagePath = null)
{
    public string CapturedAtText => CapturedAt.LocalDateTime.ToString("yyyy/MM/dd HH:mm");

    public bool HasPreviewImage => !string.IsNullOrWhiteSpace(ThumbnailImagePath);

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
