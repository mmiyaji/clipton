using Microsoft.UI.Xaml.Automation;
using Clipton.Core;
using Microsoft.UI;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using WinRT.Interop;
using Forms = System.Windows.Forms;

namespace Clipton.WinUI;

public sealed class MainWindow : Window
{
    private const string UiFontFamily = "Segoe UI Variable Text";
    private const string JapaneseUiFontFamily = "Yu Gothic UI";
    private const int HistoryDisplayBatchSize = 50;
    private const double SettingsPageMaxWidth = 920;
    private const double SidebarExpandedWidth = 248;
    private const double SidebarCollapsedWidth = 72;
    private const double ContentMinWidth = 680;
    private const double SidebarAutoCollapseWidth = 1040;
    private const double SidebarAutoExpandWidth = 1120;
    private const double SettingControlHeight = 36;
    private const string TermsUrl = "https://mmiyaji.github.io/clipton/terms/";
    private const string PrivacyUrl = "https://mmiyaji.github.io/clipton/privacy/";
    private readonly CliptonRuntime _runtime;
    private readonly Grid _root = new();
    private readonly ColumnDefinition _sidebarColumn = new() { Width = new GridLength(SidebarExpandedWidth) };
    private readonly ColumnDefinition _contentColumn = new() { Width = new GridLength(1, GridUnitType.Star), MinWidth = ContentMinWidth };
    private readonly Grid _sidebarFrame = new();
    private readonly ScrollViewer _contentScroller = new();
    private readonly StackPanel _sidebar = new() { Padding = new Thickness(18, 22, 14, 18), Spacing = 12 };
    private readonly StackPanel _generalPage = SettingsPage();
    private readonly StackPanel _historyPage = SettingsPage();
    private readonly StackPanel _historySettingsPage = SettingsPage();
    private readonly StackPanel _snippetPage = SettingsPage();
    private readonly StackPanel _aboutPage = SettingsPage();
    private readonly StackPanel _historyItemsPanel = new() { Spacing = 6 };
    private readonly StackPanel _snippetItemsPanel = new() { Spacing = 6 };
    private readonly List<Border> _cards = [];
    private readonly List<Button> _navButtons = [];
    private readonly Dictionary<ToggleSwitch, TextBlock> _toggleStateLabels = [];
    private readonly List<(TextBlock TextBlock, string Key)> _localizedTextBlocks = [];
    private readonly List<TextBlock> _descriptionTextBlocks = [];
    private readonly Button _generalNavButton = new();
    private readonly Button _historyNavButton = new();
    private readonly Button _historySettingsNavButton = new();
    private readonly Button _snippetNavButton = new();
    private readonly Button _aboutNavButton = new();
    private readonly Button _sidebarToggleButton = new();
    private readonly TextBlock _titleText = Header(20);
    private readonly TextBlock _hotkeyText = Description();
    private readonly UIElement _brandHeader;
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
    private readonly ToggleSwitch _startupToggle = CompactToggle();
    private readonly ToggleSwitch _hideSettingsWindowOnStartupToggle = CompactToggle();
    private readonly ToggleSwitch _pauseCaptureToggle = CompactToggle();
    private readonly ToggleSwitch _persistHistoryToggle = CompactToggle();
    private readonly ToggleSwitch _maskSensitiveContentToggle = CompactToggle();
    private readonly Button _maskDefinitionsButton = new();
    private readonly StackPanel _maskDefinitionsPanel = new() { Spacing = 12 };
    private readonly ComboBox _maskPrefixBox = new();
    private readonly Border _maskPatternsHost = new();
    private readonly Border _maskTestHost = new();
    private readonly TextBlock _maskDefinitionsErrorText = Description();
    private readonly TextBlock _maskTestResultText = Description();
    private readonly ComboBox _maxHistoryItemsBox = new();
    private readonly ToggleSwitch _folderModeToggle = CompactToggle();
    private readonly Button _registerFromHistoryButton = new();
    private readonly Button _exportHistoryButton = new();
    private readonly Button _importHistoryButton = new();
    private readonly Button _clearButton = new();
    private readonly Grid _historySearchHost = new();
    private readonly TextBox _historySearchBox = new();
    private readonly FontIcon _historySearchIcon = new();
    private Forms.Form? _maskPatternsOverlay;
    private Forms.Panel? _maskPatternsPanel;
    private Forms.TextBox? _maskPatternsBox;
    private Forms.Form? _maskTestOverlay;
    private Forms.Panel? _maskTestPanel;
    private Forms.TextBox? _maskTestBox;
    private readonly Button _advancedHistorySearchButton = new();
    private readonly Button _clearHistorySearchButton = new();
    private readonly Button _loadMoreHistoryButton = new();
    private readonly TextBlock _historySearchStatusText = Description();
    private readonly TextBlock _historyAdvancedSearchText = Description();
    private readonly TextBlock _selectedSnippetText = Description();
    private readonly Button _newSnippetButton = new();
    private readonly Button _exportSnippetsButton = new();
    private readonly Button _importSnippetsButton = new();
    private readonly Button _saveSnippetButton = new();
    private readonly Button _pasteSnippetButton = new();
    private readonly Button _deleteSnippetButton = new();
    private readonly Button _termsButton = new();
    private readonly Button _privacyButton = new();
    private string? _selectedHistoryId;
    private SnippetItemViewModel? _selectedSnippet;
    private string _historySearchQuery = string.Empty;
    private int _historyVisibleLimit = HistoryDisplayBatchSize;
    private bool _loading;
    private bool _updatingHistorySearchBox;
    private int _selectedPageIndex;
    private bool _sidebarCollapsed;
    private bool _autoSidebarApplied;
    private Microsoft.UI.Windowing.AppWindow? _appWindow;
    private readonly NativeMethods.WindowProc _windowProc;
    private IntPtr _hwnd;
    private IntPtr _originalWndProc;
    private bool _hiddenToTray;
    private bool _maskDefinitionsExpanded;
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
            _historyAdvancedSearchText,
            _selectedSnippetText,
            _maskDefinitionsErrorText,
            _maskTestResultText);
        Title = "Clipton";
        BuildUi();
        ApplyTheme();
        SizeWindow();
        RefreshTexts();
        RefreshItems();
        Closed += (_, _) => _runtime.OnMainWindowClosed(this);
        Closed += (_, _) => _maskPatternsOverlay?.Dispose();
        Closed += (_, _) => _maskPatternsBox?.Dispose();
        Closed += (_, _) => _maskTestOverlay?.Dispose();
        Closed += (_, _) => _maskTestBox?.Dispose();
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

    public void RefreshItems()
    {
        _historyItemsPanel.Children.Clear();
        var historyItems = _runtime.History.Items.Where(HistoryMatchesSearch).ToArray();
        var visibleHistoryItems = historyItems.Take(_historyVisibleLimit).ToArray();
        foreach (var snapshot in visibleHistoryItems)
        {
            var item = _runtime.CreateHistoryItemViewModel(snapshot);
            var button = ItemButton(item.Preview, item.FormatSummary);
            button.Click += (_, _) => _selectedHistoryId = item.Id;
            button.DoubleTapped += (_, _) => _runtime.PasteHistoryItem(item.Id, asPlainText: false);
            _historyItemsPanel.Children.Add(button);
        }

        if (visibleHistoryItems.Length == 0)
        {
            _historyItemsPanel.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(_historySearchQuery) ? _runtime.Translate("HistoryEmpty") : _runtime.Translate("NoSearchResults"),
                Foreground = DescriptionBrush(),
                Margin = new Thickness(2, 4, 2, 4)
            });
        }
        else if (historyItems.Length > visibleHistoryItems.Length)
        {
            _loadMoreHistoryButton.Content = string.Format(_runtime.Translate("LoadMoreHistory"), historyItems.Length - visibleHistoryItems.Length);
            _historyItemsPanel.Children.Add(_loadMoreHistoryButton);
        }

        _snippetItemsPanel.Children.Clear();
        foreach (var item in _runtime.Snippets.Snippets
            .OrderBy(snippet => snippet.Folder, StringComparer.OrdinalIgnoreCase)
            .ThenBy(snippet => snippet.Name, StringComparer.OrdinalIgnoreCase)
            .Select(SnippetItemViewModel.FromSnippet))
        {
            var button = ItemButton(item.DisplayName, item.Folder);
            button.Click += (_, _) => SelectSnippet(item);
            button.DoubleTapped += (_, _) => _runtime.PasteSnippet(item.Folder, item.Name);
            _snippetItemsPanel.Children.Add(button);
        }
    }

    public void RefreshTexts()
    {
        _loading = true;
        var t = _runtime.Translate;
        _titleText.Text = t("AppName");
        _hotkeyText.Text = $"{t("Hotkey")}: {_runtime.Settings.Hotkey}";
        UpdateNavButtonContents();
        UpdateSidebarToggleContent();
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
        _historyAdvancedSearchText.Text = t("SearchPrompt");
        SetCommandButton(_clearHistorySearchButton, "\uE711", t("ClearSearch"));
        _loadMoreHistoryButton.Content = string.Format(t("LoadMoreHistory"), 0);
        SetCommandButton(_newSnippetButton, "\uE710", t("NewSnippet"));
        SetCommandButton(_exportSnippetsButton, "\uEDE1", t("Export"));
        SetCommandButton(_importSnippetsButton, "\uE896", t("Import"));
        SetCommandButton(_saveSnippetButton, "\uE70F", t("EditSnippet"));
        SetCommandButton(_pasteSnippetButton, "\uE77F", t("Paste"));
        SetCommandButton(_deleteSnippetButton, "\uE74D", t("Delete"));
        _termsButton.Content = t("TermsOfUse");
        _privacyButton.Content = t("PrivacyPolicy");
        _captureHotkeyButton.Content = t("CaptureHotkey");
        _resetHotkeyButton.Content = t("ResetHotkey");
        SetComboBoxText(_themeBox, "system", t("ThemeSystem"));
        SetComboBoxText(_themeBox, "light", t("ThemeLight"));
        SetComboBoxText(_themeBox, "dark", t("ThemeDark"));
        SetComboBoxText(_localeBox, "system", t("LanguageSystem"));
        SetComboBoxText(_localeBox, "en", t("LanguageEnglish"));
        SetComboBoxText(_localeBox, "ja", t("LanguageJapanese"));
        _startupToggle.IsOn = _runtime.Settings.StartWithWindows;
        _hideSettingsWindowOnStartupToggle.IsOn = _runtime.Settings.HideSettingsWindowOnStartup;
        _pauseCaptureToggle.IsOn = _runtime.Settings.PauseCapture;
        _persistHistoryToggle.IsOn = _runtime.Settings.PersistEncryptedHistory;
        _maskSensitiveContentToggle.IsOn = _runtime.Settings.MaskSensitiveContent;
        EnsureHistoryLimitComboItem(_runtime.Settings.MaxHistoryItems);
        SetComboSelection(_maxHistoryItemsBox, _runtime.Settings.MaxHistoryItems.ToString());
        _folderModeToggle.IsOn = _runtime.Settings.FolderMode;
        RefreshToggleStateLabels();
        RefreshLocalizedTextBlocks();
        EnsureHotkeyComboItem(_runtime.Settings.Hotkey);
        SetComboSelection(_hotkeyBox, _runtime.Settings.Hotkey);
        SetComboSelection(_themeBox, _runtime.Settings.Theme);
        SetComboSelection(_localeBox, _runtime.Settings.Locale);
        UpdateSelectedSnippetText();
        UpdateHistorySearchStatus();
        UpdateMaskTestPreview();
        _loading = false;
    }

    private void BuildUi()
    {
        _root.ColumnDefinitions.Add(_sidebarColumn);
        _root.ColumnDefinitions.Add(_contentColumn);
        _root.SizeChanged += (_, e) => UpdateSidebarForWindowWidth(e.NewSize.Width);
        Content = _root;

        Grid.SetColumn(_sidebarFrame, 0);
        _sidebarFrame.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _sidebarFrame.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _sidebar.Children.Add(_brandHeader);
        _hotkeyText.Margin = new Thickness(8, 0, 8, 4);
        _sidebar.Children.Add(_hotkeyPill);
        _generalNavButton.Click += (_, _) => SelectPage(0);
        _historyNavButton.Click += (_, _) => SelectPage(1);
        _historySettingsNavButton.Click += (_, _) => SelectPage(2);
        _snippetNavButton.Click += (_, _) => SelectPage(3);
        _aboutNavButton.Click += (_, _) => SelectPage(4);
        _sidebarToggleButton.Click += (_, _) => SetSidebarCollapsed(!_sidebarCollapsed);
        _navButtons.AddRange([_generalNavButton, _historyNavButton, _historySettingsNavButton, _snippetNavButton, _aboutNavButton]);
        foreach (var button in _navButtons)
        {
            PrepareSidebarButton(button);
        }
        _sidebar.Children.Add(_generalNavButton);
        _sidebar.Children.Add(_historyNavButton);
        _sidebar.Children.Add(_historySettingsNavButton);
        _sidebar.Children.Add(_snippetNavButton);
        _sidebar.Children.Add(_aboutNavButton);
        _sidebarFrame.Children.Add(_sidebar);
        Grid.SetRow(_sidebarToggleButton, 1);
        _sidebarToggleButton.Margin = new Thickness(18, 0, 14, 18);
        PrepareSidebarButton(_sidebarToggleButton);
        _sidebarFrame.Children.Add(_sidebarToggleButton);
        _root.Children.Add(_sidebarFrame);

        _contentScroller.MinWidth = ContentMinWidth;
        _contentScroller.Padding = new Thickness(36, 30, 36, 42);
        _contentScroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        _contentScroller.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        Grid.SetColumn(_contentScroller, 1);
        var contentHost = new Grid { HorizontalAlignment = HorizontalAlignment.Left, MinWidth = ContentMinWidth };
        _contentScroller.Content = contentHost;
        _contentScroller.SizeChanged += (_, e) =>
        {
            UpdateSettingsPageWidth(Math.Max(0, e.NewSize.Width - _contentScroller.Padding.Left - _contentScroller.Padding.Right));
            PositionNativeChildInputs();
        };
        _contentScroller.ViewChanged += (_, _) => PositionNativeChildInputs();
        contentHost.Children.Add(_generalPage);
        contentHost.Children.Add(_historyPage);
        contentHost.Children.Add(_historySettingsPage);
        contentHost.Children.Add(_snippetPage);
        contentHost.Children.Add(_aboutPage);
        _root.Children.Add(_contentScroller);

        BuildGeneralPage();
        BuildHistoryPage();
        BuildHistorySettingsPage();
        BuildSnippetPage();
        BuildAboutPage();
        SelectPage(0);
    }

    private void BuildGeneralPage()
    {
        _generalPage.Children.Add(PageHeader(_generalHeaderText, _generalDescriptionText));
        _generalPage.Children.Add(SectionHeader("ActivationSection"));
        foreach (var hotkey in new[] { "Ctrl+Shift+V", "Ctrl+Alt+V", "Alt+Space" })
        {
            _hotkeyBox.Items.Add(new ComboBoxItem { Content = hotkey, Tag = hotkey });
        }

        _hotkeyBox.SelectionChanged += (_, _) =>
        {
            if (_loading || (_hotkeyBox.SelectedItem as ComboBoxItem)?.Tag is not string hotkey) return;
            TryApplyHotkey(hotkey);
        };
        _captureHotkeyButton.Click += (_, _) => CaptureCustomHotkey();
        _resetHotkeyButton.Click += (_, _) => TryApplyHotkey(HotkeyGesture.Default.ToString());
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
    }

    private void BuildHistoryPage()
    {
        _historyPage.Children.Add(PageHeader(_historyHeaderText, _historyDescriptionText));
        _historyPage.Children.Add(SectionHeader("HistorySection"));
        _historyPage.Children.Add(BuildHistorySearchPanel());

        var actions = new Grid { ColumnSpacing = 12 };
        actions.ColumnDefinitions.Add(new ColumnDefinition());
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var primaryActions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, Spacing = 8 };
        var dataActions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        _registerFromHistoryButton.Click += (_, _) => RegisterSelectedHistory();
        _clearButton.Click += (_, _) => ConfirmAndClearHistory();
        _loadMoreHistoryButton.Click += (_, _) => LoadMoreHistory();
        _exportHistoryButton.Click += (_, _) => ExportHistory();
        _importHistoryButton.Click += (_, _) => ImportHistory();
        primaryActions.Children.Add(_registerFromHistoryButton);
        dataActions.Children.Add(_exportHistoryButton);
        dataActions.Children.Add(_importHistoryButton);
        dataActions.Children.Add(_clearButton);
        actions.Children.Add(primaryActions);
        Grid.SetColumn(dataActions, 1);
        actions.Children.Add(dataActions);
        _historyPage.Children.Add(actions);
        _historyPage.Children.Add(_historySearchStatusText);
        _historyPage.Children.Add(Card(_historyItemsPanel));
    }

    private void BuildHistorySettingsPage()
    {
        _historySettingsPage.Children.Add(PageHeader(_historySettingsHeaderText, _historySettingsDescriptionText));
        _historySettingsPage.Children.Add(SectionHeader("CapturePrivacySection"));
        foreach (var toggle in new[] { _pauseCaptureToggle, _persistHistoryToggle, _maskSensitiveContentToggle })
        {
            toggle.Toggled += (_, _) => SaveHistoryOptions();
        }

        _historySettingsPage.Children.Add(SettingCard("\uE769", "PauseCapture", "PauseCaptureDescription", _pauseCaptureToggle));
        _historySettingsPage.Children.Add(SettingCard("\uE72E", "PersistHistory", "PersistHistoryDescription", _persistHistoryToggle));
        _maxHistoryItemsBox.Width = 180;
        foreach (var count in new[] { 50, 100, 200, 500, 1000 })
        {
            _maxHistoryItemsBox.Items.Add(new ComboBoxItem { Tag = count.ToString(), Content = count.ToString() });
        }

        _maxHistoryItemsBox.SelectionChanged += (_, _) => ChangeMaxHistoryItems();
        _historySettingsPage.Children.Add(SettingCard("\uE81C", "MaxHistoryItems", "MaxHistoryItemsDescription", _maxHistoryItemsBox));
        var maskControls = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        maskControls.Children.Add(_maskDefinitionsButton);
        maskControls.Children.Add(ToggleActionHost(_maskSensitiveContentToggle));
        _maskDefinitionsButton.Click += (_, _) => ToggleMaskDefinitionsPanel();
        _historySettingsPage.Children.Add(SettingCard("\uE8D7", "MaskSensitiveContent", "MaskSensitiveContentDescription", maskControls));
        _historySettingsPage.Children.Add(BuildMaskDefinitionsPanel());
    }

    private UIElement BuildHistorySearchPanel()
    {
        _historySearchHost.Height = SettingControlHeight;
        _historySearchHost.HorizontalAlignment = HorizontalAlignment.Stretch;
        _historySearchBox.Height = SettingControlHeight;
        _historySearchBox.MinHeight = SettingControlHeight;
        _historySearchBox.PlaceholderText = _runtime.Translate("SearchPlaceholder");
        _historySearchBox.Text = _historySearchQuery;
        _historySearchBox.Padding = new Thickness(36, 0, 12, 0);
        _historySearchBox.VerticalContentAlignment = VerticalAlignment.Center;
        _historySearchBox.HorizontalAlignment = HorizontalAlignment.Stretch;
        _historySearchBox.TextChanged += (_, _) =>
        {
            if (_updatingHistorySearchBox)
            {
                return;
            }

            ApplyHistorySearch(_historySearchBox.Text);
        };
        _historySearchHost.Children.Add(_historySearchBox);
        _historySearchIcon.Glyph = "\uE721";
        _historySearchIcon.FontFamily = new FontFamily("Segoe Fluent Icons");
        _historySearchIcon.FontSize = 14;
        _historySearchIcon.Foreground = DescriptionBrush();
        _historySearchIcon.IsHitTestVisible = false;
        _historySearchIcon.HorizontalAlignment = HorizontalAlignment.Left;
        _historySearchIcon.VerticalAlignment = VerticalAlignment.Center;
        _historySearchIcon.Margin = new Thickness(12, 0, 0, 0);
        _historySearchHost.Children.Add(_historySearchIcon);

        _advancedHistorySearchButton.Width = SettingControlHeight;
        _advancedHistorySearchButton.MinWidth = SettingControlHeight;
        _advancedHistorySearchButton.Height = SettingControlHeight;
        _advancedHistorySearchButton.Padding = new Thickness(0);
        _advancedHistorySearchButton.Opacity = 0.72;
        _advancedHistorySearchButton.Click += (_, _) => ToggleAdvancedHistorySearch();
        _clearHistorySearchButton.MinHeight = SettingControlHeight;
        _clearHistorySearchButton.Click += (_, _) => ClearHistorySearch();

        _historyAdvancedSearchText.Visibility = Visibility.Collapsed;
        _historyAdvancedSearchText.Margin = new Thickness(2, 0, 0, 0);

        var row = new Grid { ColumnSpacing = 12 };
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
                _historyAdvancedSearchText
            }
        };
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

        _maskPatternsHost.MinHeight = 120;
        _maskPatternsHost.HorizontalAlignment = HorizontalAlignment.Stretch;
        _maskPatternsHost.Background = CardBackground();
        _maskPatternsHost.BorderThickness = new Thickness(0);
        _maskPatternsHost.CornerRadius = new CornerRadius(4);
        _maskPatternsHost.Loaded += (_, _) => EnsureNativeMaskPatternsBox();
        _maskPatternsHost.SizeChanged += (_, _) => PositionNativeChildInputs();

        _maskTestHost.MinHeight = 64;
        _maskTestHost.HorizontalAlignment = HorizontalAlignment.Stretch;
        _maskTestHost.Background = CardBackground();
        _maskTestHost.BorderThickness = new Thickness(0);
        _maskTestHost.CornerRadius = new CornerRadius(4);
        _maskTestHost.Loaded += (_, _) => EnsureNativeMaskTestBox();
        _maskTestHost.SizeChanged += (_, _) => PositionNativeChildInputs();
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
        _maskDefinitionsPanel.Children.Add(_maskPatternsHost);
        _maskDefinitionsPanel.Children.Add(LocalizedText("MaskTestText", fontWeight: Microsoft.UI.Text.FontWeights.SemiBold));
        _maskDefinitionsPanel.Children.Add(DescriptionText("MaskTestDescription", fontSize: 12, wrapping: TextWrapping.Wrap));
        _maskDefinitionsPanel.Children.Add(_maskTestHost);
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
            EnsureNativeMaskPatternsBox();
            ScrollMaskDefinitionsIntoViewAndFocus();
        }
        QueueNativeChildInputReposition();
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

            var target = _maskPatternsHost.ActualHeight > 0 ? _maskPatternsHost : _maskDefinitionsCard;
            var point = target.TransformToVisual(_contentScroller).TransformPoint(new Windows.Foundation.Point(0, 0));
            var targetOffset = Math.Max(0, _contentScroller.VerticalOffset + point.Y - 28);
            _contentScroller.ChangeView(null, targetOffset, null, true);
            PositionNativeChildInputs();
        });

        await Task.Delay(180);
        DispatcherQueue.TryEnqueue(() =>
        {
            PositionNativeChildInputs();
            _maskPatternsBox?.Focus();
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
        return (_maskPatternsBox?.Text ?? string.Empty)
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

        var testText = _maskTestBox?.Text ?? string.Empty;
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

    private void EnsureNativeMaskPatternsBox()
    {
        if (_maskPatternsBox is not null || _hwnd == IntPtr.Zero)
        {
            PositionNativeChildInputs();
            return;
        }

        var backColor = HistorySearchBackColor();
        _maskPatternsOverlay = new Forms.Form
        {
            FormBorderStyle = Forms.FormBorderStyle.None,
            ShowInTaskbar = false,
            StartPosition = Forms.FormStartPosition.Manual,
            BackColor = backColor,
            Padding = new Forms.Padding(0),
            TopMost = false,
            Visible = false
        };
        _maskPatternsPanel = new Forms.Panel
        {
            BackColor = backColor,
            Dock = Forms.DockStyle.Fill,
            Padding = new Forms.Padding(12)
        };
        _maskPatternsPanel.Paint += (_, args) => PaintNativeInputBorder(args.Graphics, _maskPatternsPanel, _maskPatternsBox);
        _maskPatternsBox = new Forms.TextBox
        {
            Dock = Forms.DockStyle.Fill,
            BorderStyle = Forms.BorderStyle.None,
            Multiline = true,
            AcceptsReturn = true,
            AcceptsTab = true,
            ScrollBars = Forms.ScrollBars.None,
            WordWrap = false,
            Font = DialogFont(10f),
            Text = string.Join(Environment.NewLine, _runtime.Settings.CustomMaskPatterns),
            BackColor = backColor,
            ForeColor = HistorySearchForeColor(),
            Visible = true
        };
        _maskPatternsBox.Enter += (_, _) => _maskPatternsPanel.Invalidate();
        _maskPatternsBox.Leave += (_, _) => _maskPatternsPanel.Invalidate();
        _maskPatternsBox.TextChanged += (_, _) => UpdateMaskTestPreview();
        _maskPatternsPanel.Controls.Add(_maskPatternsBox);
        _maskPatternsOverlay.Controls.Add(_maskPatternsPanel);
        _maskPatternsOverlay.Show(new WindowHandle(_hwnd));
        _maskPatternsOverlay.Hide();
        PositionNativeChildInputs();
    }

    private void EnsureNativeMaskTestBox()
    {
        if (_maskTestBox is not null || _hwnd == IntPtr.Zero)
        {
            PositionNativeChildInputs();
            return;
        }

        var backColor = HistorySearchBackColor();
        _maskTestOverlay = new Forms.Form
        {
            FormBorderStyle = Forms.FormBorderStyle.None,
            ShowInTaskbar = false,
            StartPosition = Forms.FormStartPosition.Manual,
            BackColor = backColor,
            Padding = new Forms.Padding(0),
            TopMost = false,
            Visible = false
        };
        _maskTestPanel = new Forms.Panel
        {
            BackColor = backColor,
            Dock = Forms.DockStyle.Fill,
            Padding = new Forms.Padding(12, 8, 12, 8)
        };
        _maskTestPanel.Paint += (_, args) => PaintNativeInputBorder(args.Graphics, _maskTestPanel, _maskTestBox);
        _maskTestBox = new Forms.TextBox
        {
            Dock = Forms.DockStyle.Fill,
            BorderStyle = Forms.BorderStyle.None,
            Multiline = true,
            AcceptsReturn = true,
            ScrollBars = Forms.ScrollBars.None,
            WordWrap = true,
            Font = DialogFont(10f),
            BackColor = backColor,
            ForeColor = HistorySearchForeColor(),
            Visible = true
        };
        _maskTestBox.Enter += (_, _) => _maskTestPanel.Invalidate();
        _maskTestBox.Leave += (_, _) => _maskTestPanel.Invalidate();
        _maskTestBox.TextChanged += (_, _) => UpdateMaskTestPreview();
        _maskTestPanel.Controls.Add(_maskTestBox);
        _maskTestOverlay.Controls.Add(_maskTestPanel);
        _maskTestOverlay.Show(new WindowHandle(_hwnd));
        _maskTestOverlay.Hide();
        PositionNativeChildInputs();
    }

    private void PositionNativeChildInputs()
    {
        PositionNativeMaskPatternsBox();
        PositionNativeMaskTestBox();
    }

    private async void QueueNativeChildInputReposition()
    {
        PositionNativeChildInputs();
        await Task.Delay(120);
        DispatcherQueue.TryEnqueue(PositionNativeChildInputs);
        await Task.Delay(300);
        DispatcherQueue.TryEnqueue(PositionNativeChildInputs);
    }

    private void PositionNativeMaskPatternsBox()
    {
        if (_maskPatternsOverlay is null || _maskPatternsOverlay.IsDisposed || _hwnd == IntPtr.Zero)
        {
            return;
        }

        if (_selectedPageIndex != 2 || !_maskDefinitionsExpanded || !TryGetVisibleNativeChildBounds(_maskPatternsHost, out var x, out var y, out var width, out var height))
        {
            _maskPatternsOverlay.Hide();
            return;
        }

        _maskPatternsOverlay.SetBounds(x, y, width, height);
        if (!_maskPatternsOverlay.Visible)
        {
            _maskPatternsOverlay.Show(new WindowHandle(_hwnd));
        }
    }

    private void PositionNativeMaskTestBox()
    {
        if (_maskTestOverlay is null || _maskTestOverlay.IsDisposed || _hwnd == IntPtr.Zero)
        {
            return;
        }

        if (_selectedPageIndex != 2 || !_maskDefinitionsExpanded || !TryGetVisibleNativeChildBounds(_maskTestHost, out var x, out var y, out var width, out var height))
        {
            _maskTestOverlay.Hide();
            return;
        }

        _maskTestOverlay.SetBounds(x, y, width, height);
        if (!_maskTestOverlay.Visible)
        {
            _maskTestOverlay.Show(new WindowHandle(_hwnd));
        }
    }

    private bool TryGetVisibleNativeChildBounds(FrameworkElement host, out int x, out int y, out int width, out int height)
    {
        x = 0;
        y = 0;
        width = 0;
        height = 0;
        if (_contentScroller.ActualWidth <= 0 || _contentScroller.ActualHeight <= 0 || host.ActualWidth <= 0 || host.ActualHeight <= 0 || _hwnd == IntPtr.Zero)
        {
            return false;
        }

        var hostPoint = host.TransformToVisual(_contentScroller).TransformPoint(new Windows.Foundation.Point(0, 0));
        var hostRight = hostPoint.X + host.ActualWidth;
        var hostBottom = hostPoint.Y + host.ActualHeight;
        var visibleLeft = Math.Max(0, hostPoint.X);
        var visibleTop = Math.Max(0, hostPoint.Y);
        var visibleRight = Math.Min(_contentScroller.ActualWidth, hostRight);
        var visibleBottom = Math.Min(_contentScroller.ActualHeight, hostBottom);
        if (visibleRight <= visibleLeft || visibleBottom <= visibleTop)
        {
            return false;
        }

        var rootPoint = _contentScroller.TransformToVisual(_root).TransformPoint(new Windows.Foundation.Point(visibleLeft, visibleTop));
        var scale = NativeMethods.GetDpiForWindow(_hwnd) / 96.0;
        var screenPoint = new NativeMethods.Point
        {
            X = (int)Math.Round(rootPoint.X * scale),
            Y = (int)Math.Round(rootPoint.Y * scale)
        };
        if (!NativeMethods.ClientToScreen(_hwnd, ref screenPoint))
        {
            return false;
        }

        x = screenPoint.X;
        y = screenPoint.Y;
        width = Math.Max(1, (int)Math.Round((visibleRight - visibleLeft) * scale));
        height = Math.Max(1, (int)Math.Round((visibleBottom - visibleTop) * scale));
        return true;
    }

    private void PaintNativeInputBorder(System.Drawing.Graphics graphics, Forms.Control control, Forms.Control? textBox)
    {
        graphics.Clear(HistorySearchBackColor());
        var borderColor = ReferenceEquals(control, _maskPatternsPanel) || ReferenceEquals(control, _maskTestPanel)
            ? HistorySearchSubtleBorderColor()
            : HistorySearchBorderColor();
        using var borderPen = new System.Drawing.Pen(borderColor);
        graphics.DrawRectangle(borderPen, 0, 0, control.ClientSize.Width - 1, control.ClientSize.Height - 1);
    }

    private System.Drawing.Color HistorySearchBackColor() => IsDark
        ? System.Drawing.Color.FromArgb(45, 45, 45)
        : System.Drawing.Color.White;

    private System.Drawing.Color HistorySearchForeColor() => IsDark
        ? System.Drawing.Color.FromArgb(243, 243, 243)
        : System.Drawing.Color.FromArgb(31, 31, 31);

    private System.Drawing.Color HistorySearchBorderColor() => IsDark
        ? System.Drawing.Color.FromArgb(82, 82, 82)
        : System.Drawing.Color.FromArgb(138, 138, 138);

    private System.Drawing.Color HistorySearchSubtleBorderColor() => IsDark
        ? System.Drawing.Color.FromArgb(68, 68, 68)
        : System.Drawing.Color.FromArgb(154, 154, 154);

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
        _historyAdvancedSearchText.Visibility = _historyAdvancedSearchText.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
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
            ? string.Format(_runtime.Translate("SearchResults"), _historySearchQuery)
            : string.Empty;
        _historySearchStatusText.Visibility = hasQuery ? Visibility.Visible : Visibility.Collapsed;
        _clearHistorySearchButton.Visibility = hasQuery ? Visibility.Visible : Visibility.Collapsed;
        if (!string.Equals(_historySearchBox.Text, _historySearchQuery, StringComparison.Ordinal))
        {
            _updatingHistorySearchBox = true;
            _historySearchBox.Text = _historySearchQuery;
            _updatingHistorySearchBox = false;
        }
    }

    private void BuildSnippetPage()
    {
        _snippetPage.Children.Add(PageHeader(_snippetHeaderText, _snippetDescriptionText));
        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(Card(_snippetItemsPanel));

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
        _newSnippetButton.Click += (_, _) => OpenSnippetEditor(null, string.Empty, string.Empty, string.Empty);
        _saveSnippetButton.Click += (_, _) =>
        {
            if (_selectedSnippet is null) return;
            var snippet = _runtime.Snippets.Find(_selectedSnippet.Folder, _selectedSnippet.Name);
            OpenSnippetEditor(_selectedSnippet, snippet?.Folder ?? _selectedSnippet.Folder, snippet?.Name ?? _selectedSnippet.Name, snippet?.Text ?? string.Empty);
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
        _exportSnippetsButton.Click += (_, _) => ExportSnippets();
        _importSnippetsButton.Click += (_, _) => ImportSnippets();
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
        info.Children.Add(InfoRow("Publisher", "Clipton"));
        info.Children.Add(InfoRow("Author", "Clipton contributors"));
        info.Children.Add(InfoRow("License", string.Empty, "LicenseValue"));
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

    private void TryApplyHotkey(string hotkey)
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

        Forms.MessageBox.Show(
            _runtime.Translate("HotkeyUnavailable"),
            _runtime.Translate("Hotkey"),
            Forms.MessageBoxButtons.OK,
            Forms.MessageBoxIcon.Warning);
        RefreshTexts();
    }

    private void CaptureCustomHotkey()
    {
        var hotkey = PromptForHotkey();
        if (hotkey is not null)
        {
            TryApplyHotkey(hotkey);
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

    private void ConfirmAndClearHistory()
    {
        var result = Forms.MessageBox.Show(
            _runtime.Translate("ConfirmClearHistoryMessage"),
            _runtime.Translate("ConfirmClearHistoryTitle"),
            Forms.MessageBoxButtons.YesNo,
            Forms.MessageBoxIcon.Warning,
            Forms.MessageBoxDefaultButton.Button2);
        if (result != Forms.DialogResult.Yes)
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

    private void RegisterSelectedHistory()
    {
        if (_selectedHistoryId is null) return;
        var item = _runtime.History.Find(_selectedHistoryId);
        if (string.IsNullOrWhiteSpace(item?.Text)) return;
        SelectPage(3);
        OpenSnippetEditor(null, "History", CreateSnippetName(item.Text), item.Text);
    }

    private void ExportHistory()
    {
        var path = SelectExportPath($"clipton-history-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        if (path is null)
        {
            return;
        }

        RunImportExportAction(
            "ExportHistory",
            () => string.Format(_runtime.Translate("ExportComplete"), _runtime.ExportHistory(path)));
    }

    private void ImportHistory()
    {
        var path = SelectImportPath();
        if (path is null)
        {
            return;
        }

        RunImportExportAction(
            "ImportHistory",
            () =>
            {
                var count = _runtime.ImportHistory(path);
                RefreshItems();
                return string.Format(_runtime.Translate("ImportComplete"), count);
            });
    }

    private void ExportSnippets()
    {
        var path = SelectExportPath($"clipton-snippets-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        if (path is null)
        {
            return;
        }

        RunImportExportAction(
            "ExportSnippets",
            () => string.Format(_runtime.Translate("ExportComplete"), _runtime.ExportSnippets(path)));
    }

    private void ImportSnippets()
    {
        var path = SelectImportPath();
        if (path is null)
        {
            return;
        }

        RunImportExportAction(
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

    private string? SelectExportPath(string fileName)
    {
        using var dialog = new Forms.SaveFileDialog
        {
            Title = _runtime.Translate("Export"),
            FileName = fileName,
            DefaultExt = "json",
            AddExtension = true,
            Filter = JsonFileFilter()
        };

        return dialog.ShowDialog() == Forms.DialogResult.OK ? dialog.FileName : null;
    }

    private string? SelectImportPath()
    {
        using var dialog = new Forms.OpenFileDialog
        {
            Title = _runtime.Translate("Import"),
            DefaultExt = "json",
            CheckFileExists = true,
            Filter = JsonFileFilter()
        };

        return dialog.ShowDialog() == Forms.DialogResult.OK ? dialog.FileName : null;
    }

    private string JsonFileFilter()
    {
        return $"{_runtime.Translate("JsonFiles")} (*.json)|*.json|{_runtime.Translate("AllFiles")} (*.*)|*.*";
    }

    private void RunImportExportAction(string titleKey, Func<string> action)
    {
        try
        {
            Forms.MessageBox.Show(
                action(),
                _runtime.Translate(titleKey),
                Forms.MessageBoxButtons.OK,
                Forms.MessageBoxIcon.Information);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException)
        {
            Forms.MessageBox.Show(
                string.Format(_runtime.Translate("ImportExportFailed"), ex.Message),
                _runtime.Translate(titleKey),
                Forms.MessageBoxButtons.OK,
                Forms.MessageBoxIcon.Error);
        }
    }

    private void SelectSnippet(SnippetItemViewModel selected)
    {
        _selectedSnippet = selected;
        UpdateSelectedSnippetText();
    }

    private void UpdateSelectedSnippetText()
    {
        _selectedSnippetText.Text = _selectedSnippet is null
            ? _runtime.Translate("SnippetEditorEmpty")
            : $"{_selectedSnippet.DisplayName}\n{_selectedSnippet.Folder}";
    }

    private void OpenSnippetEditor(SnippetItemViewModel? existing, string folder, string name, string text)
    {
        using var form = new Forms.Form
        {
            Text = _runtime.Translate("SnippetEditor"),
            Icon = AppAssets.LoadAppIcon(_runtime.EffectiveTheme),
            Width = 560,
            Height = 460,
            MinimizeBox = false,
            MaximizeBox = false,
            FormBorderStyle = Forms.FormBorderStyle.FixedDialog,
            StartPosition = Forms.FormStartPosition.CenterScreen,
            BackColor = IsDark ? System.Drawing.Color.FromArgb(32, 32, 32) : System.Drawing.Color.White,
            ForeColor = IsDark ? System.Drawing.Color.White : System.Drawing.Color.Black
        };

        var table = new Forms.TableLayoutPanel
        {
            Dock = Forms.DockStyle.Fill,
            Padding = new Forms.Padding(18),
            ColumnCount = 2,
            RowCount = 5
        };
        table.ColumnStyles.Add(new Forms.ColumnStyle(Forms.SizeType.Absolute, 96));
        table.ColumnStyles.Add(new Forms.ColumnStyle(Forms.SizeType.Percent, 100));
        table.RowStyles.Add(new Forms.RowStyle(Forms.SizeType.Absolute, 36));
        table.RowStyles.Add(new Forms.RowStyle(Forms.SizeType.Absolute, 36));
        table.RowStyles.Add(new Forms.RowStyle(Forms.SizeType.Percent, 100));
        table.RowStyles.Add(new Forms.RowStyle(Forms.SizeType.Absolute, 12));
        table.RowStyles.Add(new Forms.RowStyle(Forms.SizeType.Absolute, 44));

        var folderBox = DialogTextBox(folder);
        var nameBox = DialogTextBox(name);
        var textBox = DialogTextBox(text);
        textBox.Multiline = true;
        textBox.ScrollBars = Forms.ScrollBars.Vertical;
        textBox.AcceptsReturn = true;

        table.Controls.Add(DialogLabel(_runtime.Translate("SnippetFolder")), 0, 0);
        table.Controls.Add(folderBox, 1, 0);
        table.Controls.Add(DialogLabel(_runtime.Translate("SnippetName")), 0, 1);
        table.Controls.Add(nameBox, 1, 1);
        table.Controls.Add(DialogLabel(_runtime.Translate("SnippetText")), 0, 2);
        table.Controls.Add(textBox, 1, 2);

        var buttons = new Forms.FlowLayoutPanel
        {
            FlowDirection = Forms.FlowDirection.RightToLeft,
            Dock = Forms.DockStyle.Fill
        };
        var saveButton = new Forms.Button { Text = _runtime.Translate("Save"), DialogResult = Forms.DialogResult.OK, Width = 96 };
        var cancelButton = new Forms.Button { Text = _runtime.Translate("Cancel"), DialogResult = Forms.DialogResult.Cancel, Width = 96 };
        buttons.Controls.Add(saveButton);
        buttons.Controls.Add(cancelButton);
        table.Controls.Add(buttons, 0, 4);
        table.SetColumnSpan(buttons, 2);

        form.Controls.Add(table);
        form.AcceptButton = saveButton;
        form.CancelButton = cancelButton;
        form.Shown += (_, _) => ApplyFormTitleBarTheme(form);
        if (form.ShowDialog() != Forms.DialogResult.OK)
        {
            return;
        }

        var newName = nameBox.Text.Trim();
        var newText = textBox.Text;
        if (string.IsNullOrWhiteSpace(newName) || string.IsNullOrWhiteSpace(newText))
        {
            return;
        }

        var newFolder = folderBox.Text.Trim();
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

    private Forms.Label DialogLabel(string text) => new()
    {
        Text = text,
        Dock = Forms.DockStyle.Fill,
        AutoSize = false,
        TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
        Font = DialogFont(9f),
        ForeColor = IsDark ? System.Drawing.Color.FromArgb(220, 220, 220) : System.Drawing.Color.FromArgb(42, 42, 42)
    };

    private Forms.Label DialogTitleLabel(string text) => new()
    {
        Text = text,
        Dock = Forms.DockStyle.Fill,
        AutoSize = false,
        TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
        Font = DialogFont(10.5f, System.Drawing.FontStyle.Regular),
        ForeColor = DialogForegroundColor()
    };

    private Forms.Label DialogDescriptionLabel(string text) => new()
    {
        Text = text,
        Dock = Forms.DockStyle.Fill,
        AutoSize = false,
        TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
        Font = DialogFont(9f),
        ForeColor = DialogMutedColor()
    };

    private Forms.TextBox DialogTextBox(string text) => new()
    {
        Text = text,
        Dock = Forms.DockStyle.Fill,
        Font = DialogFont(10.5f),
        BackColor = DialogInputColor(),
        ForeColor = DialogForegroundColor(),
        BorderStyle = Forms.BorderStyle.FixedSingle
    };

    private Forms.Panel DialogPanel() => new()
    {
        Dock = Forms.DockStyle.Fill,
        Padding = new Forms.Padding(14),
        BackColor = DialogCardColor()
    };

    private Forms.Button DialogButton(string text, bool primary)
    {
        var button = new Forms.Button
        {
            Text = text,
            Width = 96,
            Height = 32,
            FlatStyle = Forms.FlatStyle.Flat,
            Font = DialogFont(9f),
            BackColor = primary ? DialogAccentColor() : DialogButtonColor(),
            ForeColor = primary ? System.Drawing.Color.White : DialogForegroundColor()
        };
        button.FlatAppearance.BorderColor = primary ? DialogAccentColor() : DialogBorderColor();
        button.FlatAppearance.BorderSize = 1;
        return button;
    }

    private System.Drawing.Color DialogBackgroundColor() => IsDark
        ? System.Drawing.Color.FromArgb(31, 31, 31)
        : System.Drawing.Color.FromArgb(243, 243, 243);

    private System.Drawing.Color DialogCardColor() => IsDark
        ? System.Drawing.Color.FromArgb(43, 43, 43)
        : System.Drawing.Color.White;

    private System.Drawing.Color DialogInputColor() => IsDark
        ? System.Drawing.Color.FromArgb(36, 36, 36)
        : System.Drawing.Color.White;

    private System.Drawing.Color DialogButtonColor() => IsDark
        ? System.Drawing.Color.FromArgb(49, 49, 49)
        : System.Drawing.Color.FromArgb(251, 251, 251);

    private System.Drawing.Color DialogForegroundColor() => IsDark
        ? System.Drawing.Color.FromArgb(243, 243, 243)
        : System.Drawing.Color.FromArgb(31, 31, 31);

    private System.Drawing.Color DialogMutedColor() => IsDark
        ? System.Drawing.Color.FromArgb(200, 200, 200)
        : System.Drawing.Color.FromArgb(96, 96, 96);

    private System.Drawing.Color DialogBorderColor() => IsDark
        ? System.Drawing.Color.FromArgb(72, 72, 72)
        : System.Drawing.Color.FromArgb(205, 205, 205);

    private static System.Drawing.Color DialogAccentColor() => System.Drawing.Color.FromArgb(0, 120, 212);

    private System.Drawing.Font DialogFont(float size, System.Drawing.FontStyle style = System.Drawing.FontStyle.Regular)
    {
        var family = IsJapaneseLocale() ? JapaneseUiFontFamily : UiFontFamily;
        return new System.Drawing.Font(family, size, style, System.Drawing.GraphicsUnit.Point);
    }

    private bool IsJapaneseLocale()
    {
        var locale = _runtime.Settings.Locale;
        if (string.IsNullOrWhiteSpace(locale) || string.Equals(locale, "auto", StringComparison.OrdinalIgnoreCase))
        {
            locale = CultureInfo.CurrentUICulture.Name;
        }

        return locale.StartsWith("ja", StringComparison.OrdinalIgnoreCase);
    }

    private string? PromptForHotkey()
    {
        _runtime.SuspendHotkey();
        using var form = new Forms.Form
        {
            Text = _runtime.Translate("CaptureHotkey"),
            Icon = AppAssets.LoadAppIcon(_runtime.EffectiveTheme),
            Width = 460,
            Height = 180,
            MinimizeBox = false,
            MaximizeBox = false,
            FormBorderStyle = Forms.FormBorderStyle.FixedDialog,
            StartPosition = Forms.FormStartPosition.CenterScreen,
            KeyPreview = true,
            BackColor = IsDark ? System.Drawing.Color.FromArgb(32, 32, 32) : System.Drawing.Color.White,
            ForeColor = IsDark ? System.Drawing.Color.White : System.Drawing.Color.Black
        };

        var layout = new Forms.TableLayoutPanel
        {
            Dock = Forms.DockStyle.Fill,
            Padding = new Forms.Padding(16),
            ColumnCount = 1,
            RowCount = 3
        };
        layout.RowStyles.Add(new Forms.RowStyle(Forms.SizeType.Absolute, 34));
        layout.RowStyles.Add(new Forms.RowStyle(Forms.SizeType.Absolute, 36));
        layout.RowStyles.Add(new Forms.RowStyle(Forms.SizeType.Absolute, 44));

        var captured = string.Empty;
        var preview = DialogTextBox(string.Empty);
        preview.ReadOnly = true;
        preview.TextAlign = Forms.HorizontalAlignment.Center;
        var okButton = new Forms.Button { Text = _runtime.Translate("Save"), DialogResult = Forms.DialogResult.OK, Width = 96, Enabled = false };
        var cancelButton = new Forms.Button { Text = _runtime.Translate("Cancel"), DialogResult = Forms.DialogResult.Cancel, Width = 96 };
        var buttons = new Forms.FlowLayoutPanel
        {
            FlowDirection = Forms.FlowDirection.RightToLeft,
            Dock = Forms.DockStyle.Fill
        };
        buttons.Controls.Add(okButton);
        buttons.Controls.Add(cancelButton);

        void CaptureKey(Forms.KeyEventArgs e)
        {
            e.SuppressKeyPress = true;
            var hotkey = BuildHotkeyFromKeyEvent(e);
            if (hotkey is null)
            {
                preview.Text = _runtime.Translate("HotkeyInvalid");
                okButton.Enabled = false;
                captured = string.Empty;
                return;
            }

            preview.Text = hotkey;
            captured = hotkey;
            okButton.Enabled = true;
        }

        form.KeyDown += (_, e) => CaptureKey(e);
        preview.KeyDown += (_, e) => CaptureKey(e);

        layout.Controls.Add(DialogLabel(_runtime.Translate("CaptureHotkeyPrompt")), 0, 0);
        layout.Controls.Add(preview, 0, 1);
        layout.Controls.Add(buttons, 0, 2);
        form.Controls.Add(layout);
        form.AcceptButton = okButton;
        form.CancelButton = cancelButton;
        form.Shown += (_, _) =>
        {
            ApplyFormTitleBarTheme(form);
            preview.Focus();
        };

        try
        {
            return form.ShowDialog() == Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(captured) ? captured : null;
        }
        finally
        {
            _runtime.RestoreHotkey();
        }
    }

    private static string? BuildHotkeyFromKeyEvent(Forms.KeyEventArgs e)
    {
        var parts = new List<string>();
        if (e.Control) parts.Add("Ctrl");
        if (e.Shift) parts.Add("Shift");
        if (e.Alt) parts.Add("Alt");

        var key = e.KeyCode switch
        {
            Forms.Keys.ControlKey or Forms.Keys.ShiftKey or Forms.Keys.Menu or Forms.Keys.LControlKey or Forms.Keys.RControlKey or Forms.Keys.LShiftKey or Forms.Keys.RShiftKey or Forms.Keys.LMenu or Forms.Keys.RMenu => null,
            Forms.Keys.Space => "Space",
            >= Forms.Keys.A and <= Forms.Keys.Z => e.KeyCode.ToString(),
            >= Forms.Keys.D0 and <= Forms.Keys.D9 => ((char)('0' + e.KeyCode - Forms.Keys.D0)).ToString(),
            >= Forms.Keys.F1 and <= Forms.Keys.F24 => e.KeyCode.ToString(),
            _ => null
        };

        if (key is null || !parts.Any(part => part is "Ctrl" or "Alt"))
        {
            return null;
        }

        parts.Add(key);
        return string.Join("+", parts);
    }

    private void SelectPage(int index)
    {
        _selectedPageIndex = index;
        _generalPage.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
        _historyPage.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
        _historySettingsPage.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
        _snippetPage.Visibility = index == 3 ? Visibility.Visible : Visibility.Collapsed;
        _aboutPage.Visibility = index == 4 ? Visibility.Visible : Visibility.Collapsed;
        for (var i = 0; i < _navButtons.Count; i++)
        {
            _navButtons[i].Background = i == index ? AccentBrush(34) : Brush("#00FFFFFF");
            _navButtons[i].BorderBrush = i == index ? AccentBrush(68) : Brush("#00FFFFFF");
        }

        UpdateSidebarToggleStyle();
        PositionNativeChildInputs();
    }

    private void SetSidebarCollapsed(bool collapsed)
    {
        if (_sidebarCollapsed == collapsed)
        {
            return;
        }

        _sidebarCollapsed = collapsed;
        _sidebarColumn.Width = new GridLength(collapsed ? SidebarCollapsedWidth : SidebarExpandedWidth);
        _sidebar.Padding = collapsed ? new Thickness(10, 22, 10, 18) : new Thickness(18, 22, 14, 18);
        _sidebar.Spacing = collapsed ? 10 : 12;
        _titleText.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        _hotkeyPill.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        _sidebarToggleButton.Margin = collapsed ? new Thickness(10, 0, 10, 18) : new Thickness(18, 0, 14, 18);
        foreach (var button in _navButtons)
        {
            button.HorizontalContentAlignment = collapsed ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
            button.Padding = collapsed ? new Thickness(9) : new Thickness(10, 9, 10, 9);
        }

        UpdateNavButtonContents();
        UpdateSidebarToggleContent();
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
        SetNavButtonContent(_generalNavButton, "\uE80F", "General");
        SetNavButtonContent(_historyNavButton, "\uE81C", "History");
        SetNavButtonContent(_historySettingsNavButton, "\uE713", "HistorySettings");
        SetNavButtonContent(_snippetNavButton, "\uE8C8", "Snippets");
        SetNavButtonContent(_aboutNavButton, "\uE946", "About");
    }

    private void SetNavButtonContent(Button button, string glyph, string labelKey)
    {
        var label = _runtime.Translate(labelKey);
        button.Content = CreateSidebarButtonContent(glyph, label);
        AutomationProperties.SetName(button, label);
        ToolTipService.SetToolTip(button, label);
    }

    private void UpdateSidebarToggleContent()
    {
        var key = _sidebarCollapsed ? "SidebarExpand" : "SidebarCollapse";
        var label = _runtime.Translate(key);
        _sidebarToggleButton.Content = CreateSidebarButtonContent(_sidebarCollapsed ? "\uE76C" : "\uE76B", label);
        _sidebarToggleButton.HorizontalContentAlignment = _sidebarCollapsed ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
        _sidebarToggleButton.Padding = _sidebarCollapsed ? new Thickness(9) : new Thickness(10, 9, 10, 9);
        UpdateSidebarToggleStyle();
        ToolTipService.SetToolTip(_sidebarToggleButton, label);
    }

    private void UpdateSidebarToggleStyle()
    {
        _sidebarToggleButton.Background = Brush("#00FFFFFF");
        _sidebarToggleButton.BorderBrush = Brush("#00FFFFFF");
        _sidebarToggleButton.Foreground = DescriptionBrush();
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

    private static void PrepareSidebarButton(Button button)
    {
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        button.Padding = new Thickness(10, 9, 10, 9);
        button.BorderThickness = new Thickness(1);
        button.UseSystemFocusVisuals = false;
        button.Transitions = new Microsoft.UI.Xaml.Media.Animation.TransitionCollection();
    }

    private UIElement CreateSidebarButtonContent(string glyph, string label)
    {
        if (_sidebarCollapsed)
        {
            return new FontIcon
            {
                Glyph = glyph,
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                FontSize = 16
            };
        }

        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(new FontIcon
        {
            Glyph = glyph,
            FontFamily = new FontFamily("Segoe Fluent Icons"),
            FontSize = 16,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });
        var text = new TextBlock
        {
            Text = label,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        return grid;
    }

    private void ApplyTheme()
    {
        var dark = IsDark;
        _root.RequestedTheme = dark ? ElementTheme.Dark : ElementTheme.Light;
        _root.Background = Brush(dark ? "#202020" : "#F5F5F5");
        _sidebar.Background = Brush(dark ? "#171717" : "#F7F7F7");
        _sidebarFrame.Background = Brush(dark ? "#171717" : "#F7F7F7");
        ApplyTitleBarTheme();
        ApplyWindowIcon();
        foreach (var card in _cards)
        {
            card.Background = CardBackground();
            card.BorderBrush = CardBorderBrush();
        }
        RefreshThemeTextBrushes();
        RefreshNativeInputTheme();
        SelectPage(_selectedPageIndex);
        UpdateSidebarToggleContent();
        PositionNativeChildInputs();
    }

    private void RefreshNativeInputTheme()
    {
        _maskPatternsHost.Background = CardBackground();
        _maskPatternsHost.BorderBrush = CardBorderBrush();
        _maskTestHost.Background = CardBackground();
        _maskTestHost.BorderBrush = CardBorderBrush();
        var backColor = HistorySearchBackColor();
        var foreColor = HistorySearchForeColor();
        if (_maskPatternsBox is not null)
        {
            _maskPatternsOverlay!.BackColor = backColor;
            _maskPatternsPanel!.BackColor = backColor;
            _maskPatternsBox.BackColor = backColor;
            _maskPatternsBox.ForeColor = foreColor;
            _maskPatternsPanel.Invalidate();
        }

        if (_maskTestBox is not null)
        {
            _maskTestOverlay!.BackColor = backColor;
            _maskTestPanel!.BackColor = backColor;
            _maskTestBox.BackColor = backColor;
            _maskTestBox.ForeColor = foreColor;
            _maskTestPanel.Invalidate();
        }
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
        var card = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(18),
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

    private void ShowDocumentDialog(string title, string text)
    {
        using var form = new Forms.Form
        {
            Text = title,
            Icon = AppAssets.LoadAppIcon(_runtime.EffectiveTheme),
            Width = 640,
            Height = 520,
            MinimizeBox = false,
            MaximizeBox = false,
            FormBorderStyle = Forms.FormBorderStyle.FixedDialog,
            StartPosition = Forms.FormStartPosition.CenterScreen,
            BackColor = IsDark ? System.Drawing.Color.FromArgb(32, 32, 32) : System.Drawing.Color.White,
            ForeColor = IsDark ? System.Drawing.Color.White : System.Drawing.Color.Black
        };

        var textBox = DialogTextBox(text);
        textBox.Multiline = true;
        textBox.ReadOnly = true;
        textBox.ScrollBars = Forms.ScrollBars.Vertical;
        textBox.Dock = Forms.DockStyle.Fill;
        textBox.BorderStyle = Forms.BorderStyle.None;
        var closeButton = new Forms.Button { Text = _runtime.Translate("Close"), DialogResult = Forms.DialogResult.OK, Width = 96 };
        var buttons = new Forms.FlowLayoutPanel
        {
            FlowDirection = Forms.FlowDirection.RightToLeft,
            Dock = Forms.DockStyle.Bottom,
            Height = 48,
            Padding = new Forms.Padding(0, 8, 12, 8)
        };
        buttons.Controls.Add(closeButton);
        form.Controls.Add(textBox);
        form.Controls.Add(buttons);
        form.AcceptButton = closeButton;
        form.CancelButton = closeButton;
        form.Shown += (_, _) => ApplyFormTitleBarTheme(form);
        form.ShowDialog();
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

    private UIElement BuildBrandHeader()
    {
        var grid = new Grid { ColumnSpacing = 12, Margin = new Thickness(4, 0, 4, 8) };
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
            _maskPatternsOverlay?.Hide();
            _appWindow?.Hide();
            _hiddenToTray = true;
            return IntPtr.Zero;
        }

        if (msg is NativeMethods.WmMove or NativeMethods.WmSize)
        {
            Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()?.TryEnqueue(PositionNativeChildInputs);
        }

        return NativeMethods.CallWindowProc(_originalWndProc, hWnd, msg, wParam, lParam);
    }

    private sealed class WindowHandle(IntPtr handle) : Forms.IWin32Window
    {
        public IntPtr Handle { get; } = handle;
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

    private void ApplyFormTitleBarTheme(Forms.Form form)
    {
        if (!IsDark || form.Handle == IntPtr.Zero)
        {
            return;
        }

        var darkMode = 1;
        _ = DwmSetWindowAttribute(form.Handle, 20, ref darkMode, sizeof(int));
        var captionColor = ColorRef("#202020");
        var textColor = ColorRef("#F3F3F3");
        _ = DwmSetWindowAttribute(form.Handle, 35, ref captionColor, sizeof(int));
        _ = DwmSetWindowAttribute(form.Handle, 36, ref textColor, sizeof(int));
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

public sealed record HistoryItemViewModel(string Id, string Preview, string FormatSummary)
{
    public override string ToString() => $"{Preview}  {FormatSummary}";
}

public sealed record SnippetItemViewModel(string Folder, string Name, string DisplayName)
{
    public static SnippetItemViewModel FromSnippet(Snippet snippet)
    {
        return new SnippetItemViewModel(snippet.Folder, snippet.Name, snippet.DisplayName);
    }

    public override string ToString() => DisplayName;
}
