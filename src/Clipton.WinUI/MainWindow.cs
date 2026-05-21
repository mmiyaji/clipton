using Clipton.Core;
using Microsoft.UI;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using WinRT.Interop;
using Forms = System.Windows.Forms;

namespace Clipton.WinUI;

public sealed class MainWindow : Window
{
    private const int HistoryDisplayBatchSize = 50;
    private const string MaskedPreview = "\u2022\u2022\u2022\u2022\u2022\u2022\u2022\u2022";
    private readonly CliptonRuntime _runtime;
    private readonly Grid _root = new();
    private readonly StackPanel _sidebar = new() { Padding = new Thickness(18, 22, 14, 18), Spacing = 12 };
    private readonly StackPanel _generalPage = new() { Spacing = 18, MaxWidth = 920 };
    private readonly StackPanel _historyPage = new() { Spacing = 18, MaxWidth = 920 };
    private readonly StackPanel _snippetPage = new() { Spacing = 18, MaxWidth = 920 };
    private readonly StackPanel _aboutPage = new() { Spacing = 18, MaxWidth = 920 };
    private readonly StackPanel _historyItemsPanel = new() { Spacing = 6 };
    private readonly StackPanel _snippetItemsPanel = new() { Spacing = 6 };
    private readonly List<Border> _cards = [];
    private readonly List<Button> _navButtons = [];
    private readonly Button _generalNavButton = new();
    private readonly Button _historyNavButton = new();
    private readonly Button _snippetNavButton = new();
    private readonly Button _aboutNavButton = new();
    private readonly TextBlock _titleText = Header(20);
    private readonly TextBlock _hotkeyText = Description();
    private readonly TextBlock _generalHeaderText = Header();
    private readonly TextBlock _generalDescriptionText = Description();
    private readonly TextBlock _historyHeaderText = Header();
    private readonly TextBlock _historyDescriptionText = Description();
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
    private readonly ToggleSwitch _pauseCaptureToggle = CompactToggle();
    private readonly ToggleSwitch _persistHistoryToggle = CompactToggle();
    private readonly ToggleSwitch _maskSensitiveContentToggle = CompactToggle();
    private readonly ToggleSwitch _folderModeToggle = CompactToggle();
    private readonly ToggleSwitch _simpleContextMenuToggle = CompactToggle();
    private readonly Button _registerFromHistoryButton = new();
    private readonly Button _clearButton = new();
    private readonly Button _searchHistoryButton = new();
    private readonly Button _clearHistorySearchButton = new();
    private readonly Button _loadMoreHistoryButton = new();
    private readonly TextBlock _historySearchStatusText = Description();
    private readonly TextBlock _selectedSnippetText = Description();
    private readonly Button _newSnippetButton = new();
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
    private int _selectedPageIndex;

    public MainWindow(CliptonRuntime runtime)
    {
        _runtime = runtime;
        Title = "Clipton";
        BuildUi();
        ApplyTheme();
        SizeWindow();
        RefreshTexts();
        RefreshItems();
        Closed += (_, _) => _runtime.OnMainWindowClosed(this);
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
        _generalNavButton.Content = t("General");
        _historyNavButton.Content = t("History");
        _snippetNavButton.Content = t("Snippets");
        _aboutNavButton.Content = t("About");
        _generalHeaderText.Text = t("General");
        _generalDescriptionText.Text = t("GeneralDescription");
        _historyHeaderText.Text = t("History");
        _historyDescriptionText.Text = t("HistoryDescription");
        _snippetHeaderText.Text = t("Snippets");
        _snippetDescriptionText.Text = t("SnippetDescription");
        _snippetFormTitle.Text = t("SnippetEditor");
        _aboutHeaderText.Text = t("About");
        _aboutDescriptionText.Text = t("AboutDescription");
        _registerFromHistoryButton.Content = t("RegisterFromHistory");
        _clearButton.Content = t("ClearHistory");
        _searchHistoryButton.Content = t("Search");
        _clearHistorySearchButton.Content = t("ClearSearch");
        _loadMoreHistoryButton.Content = string.Format(t("LoadMoreHistory"), 0);
        _newSnippetButton.Content = t("NewSnippet");
        _saveSnippetButton.Content = t("EditSnippet");
        _pasteSnippetButton.Content = t("Paste");
        _deleteSnippetButton.Content = t("Delete");
        _termsButton.Content = t("TermsOfUse");
        _privacyButton.Content = t("PrivacyPolicy");
        _captureHotkeyButton.Content = t("CaptureHotkey");
        _resetHotkeyButton.Content = t("ResetHotkey");
        SetComboBoxText(_themeBox, "system", t("ThemeSystem"));
        SetComboBoxText(_themeBox, "light", t("ThemeLight"));
        SetComboBoxText(_themeBox, "dark", t("ThemeDark"));
        SetComboBoxText(_localeBox, "system", t("LanguageSystem"));
        _startupToggle.IsOn = _runtime.Settings.StartWithWindows;
        _pauseCaptureToggle.IsOn = _runtime.Settings.PauseCapture;
        _persistHistoryToggle.IsOn = _runtime.Settings.PersistEncryptedHistory;
        _maskSensitiveContentToggle.IsOn = _runtime.Settings.MaskSensitiveContent;
        _folderModeToggle.IsOn = _runtime.Settings.FolderMode;
        _simpleContextMenuToggle.IsOn = _runtime.Settings.SimpleContextMenuMode;
        EnsureHotkeyComboItem(_runtime.Settings.Hotkey);
        SetComboSelection(_hotkeyBox, _runtime.Settings.Hotkey);
        SetComboSelection(_themeBox, _runtime.Settings.Theme);
        SetComboSelection(_localeBox, _runtime.Settings.Locale);
        UpdateSelectedSnippetText();
        UpdateHistorySearchStatus();
        _loading = false;
    }

    private void BuildUi()
    {
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(248) });
        _root.ColumnDefinitions.Add(new ColumnDefinition());
        Content = _root;

        Grid.SetColumn(_sidebar, 0);
        _sidebar.Children.Add(BuildBrandHeader());
        _hotkeyText.Margin = new Thickness(8, 0, 8, 4);
        _sidebar.Children.Add(Pill(_hotkeyText));
        _generalNavButton.Click += (_, _) => SelectPage(0);
        _historyNavButton.Click += (_, _) => SelectPage(1);
        _snippetNavButton.Click += (_, _) => SelectPage(2);
        _aboutNavButton.Click += (_, _) => SelectPage(3);
        _navButtons.AddRange([_generalNavButton, _historyNavButton, _snippetNavButton, _aboutNavButton]);
        foreach (var button in _navButtons)
        {
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            button.Padding = new Thickness(10, 9, 10, 9);
            button.BorderThickness = new Thickness(1);
        }
        _sidebar.Children.Add(_generalNavButton);
        _sidebar.Children.Add(_historyNavButton);
        _sidebar.Children.Add(_snippetNavButton);
        _sidebar.Children.Add(_aboutNavButton);
        _root.Children.Add(_sidebar);

        var scroller = new ScrollViewer { Padding = new Thickness(36, 30, 36, 42), VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetColumn(scroller, 1);
        var contentHost = new Grid { HorizontalAlignment = HorizontalAlignment.Left };
        scroller.Content = contentHost;
        contentHost.Children.Add(_generalPage);
        contentHost.Children.Add(_historyPage);
        contentHost.Children.Add(_snippetPage);
        contentHost.Children.Add(_aboutPage);
        _root.Children.Add(scroller);

        BuildGeneralPage();
        BuildHistoryPage();
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
        var hotkeyControls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        _hotkeyBox.MinWidth = 180;
        hotkeyControls.Children.Add(_hotkeyBox);
        hotkeyControls.Children.Add(_captureHotkeyButton);
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
        _localeBox.Items.Add(new ComboBoxItem { Content = "English", Tag = "en" });
        _localeBox.Items.Add(new ComboBoxItem { Content = "Japanese", Tag = "ja" });
        _localeBox.SelectionChanged += (_, _) =>
        {
            if (_loading || (_localeBox.SelectedItem as ComboBoxItem)?.Tag is not string locale) return;
            _runtime.SetLocale(locale);
            RefreshTexts();
        };
        _generalPage.Children.Add(SettingCard("\uE8C1", "Language", "LanguageDescription", _localeBox));

        _startupToggle.Toggled += async (_, _) => await SetStartupAsync();
        _generalPage.Children.Add(SettingCard("\uE7C3", "Startup", "StartupDescription", _startupToggle));
    }

    private void BuildHistoryPage()
    {
        _historyPage.Children.Add(PageHeader(_historyHeaderText, _historyDescriptionText));
        _historyPage.Children.Add(SectionHeader("CapturePrivacySection"));
        foreach (var toggle in new[] { _pauseCaptureToggle, _persistHistoryToggle, _maskSensitiveContentToggle, _folderModeToggle, _simpleContextMenuToggle })
        {
            toggle.Toggled += (_, _) => SaveHistoryOptions();
        }

        _historyPage.Children.Add(SettingCard("\uE769", "PauseCapture", "PauseCaptureDescription", _pauseCaptureToggle));
        _historyPage.Children.Add(SettingCard("\uE72E", "PersistHistory", "PersistHistoryDescription", _persistHistoryToggle));
        _historyPage.Children.Add(SettingCard("\uE8D7", "MaskSensitiveContent", "MaskSensitiveContentDescription", _maskSensitiveContentToggle));
        _historyPage.Children.Add(SettingCard("\uE8B7", "FolderMode", "FolderModeDescription", _folderModeToggle));
        _historyPage.Children.Add(SettingCard("\uE8A5", "SimpleContextMenuMode", "SimpleContextMenuModeDescription", _simpleContextMenuToggle));

        _historyPage.Children.Add(SectionHeader("HistorySection"));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, Spacing = 8 };
        _registerFromHistoryButton.Click += (_, _) => RegisterSelectedHistory();
        _clearButton.Click += (_, _) => _runtime.ClearHistory();
        _searchHistoryButton.Click += (_, _) => SearchHistory();
        _clearHistorySearchButton.Click += (_, _) => ClearHistorySearch();
        _loadMoreHistoryButton.Click += (_, _) => LoadMoreHistory();
        actions.Children.Add(_searchHistoryButton);
        actions.Children.Add(_clearHistorySearchButton);
        actions.Children.Add(_registerFromHistoryButton);
        actions.Children.Add(_clearButton);
        _historyPage.Children.Add(actions);
        _historyPage.Children.Add(_historySearchStatusText);
        _historyPage.Children.Add(Card(_historyItemsPanel));
    }

    private bool HistoryMatchesSearch(ClipboardSnapshot item)
    {
        if (string.IsNullOrWhiteSpace(_historySearchQuery))
        {
            return true;
        }

        var formats = string.Join(", ", item.Formats);
        if (formats.Contains(_historySearchQuery, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var snippet = _runtime.Snippets.FindByText(item.Text);
        if (snippet is not null)
        {
            return snippet.DisplayName.Contains(_historySearchQuery, StringComparison.OrdinalIgnoreCase);
        }

        var preview = _runtime.Settings.MaskSensitiveContent && SensitiveContentDetector.ShouldMask(item.Text)
            ? MaskedPreview
            : item.Preview;
        return preview.Contains(_historySearchQuery, StringComparison.OrdinalIgnoreCase);
    }

    private void SearchHistory()
    {
        var query = PromptForText(_runtime.Translate("SearchHistory"), _runtime.Translate("SearchPrompt"), _historySearchQuery);
        if (query is null)
        {
            return;
        }

        _historySearchQuery = query.Trim();
        _historyVisibleLimit = HistoryDisplayBatchSize;
        UpdateHistorySearchStatus();
        RefreshItems();
    }

    private void ClearHistorySearch()
    {
        _historySearchQuery = string.Empty;
        _historyVisibleLimit = HistoryDisplayBatchSize;
        UpdateHistorySearchStatus();
        RefreshItems();
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
    }

    private void BuildSnippetPage()
    {
        _snippetPage.Children.Add(PageHeader(_snippetHeaderText, _snippetDescriptionText));
        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(Card(_snippetItemsPanel));

        var details = new StackPanel { Spacing = 12 };
        details.Children.Add(_snippetFormTitle);
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
        info.Children.Add(InfoRow("Package", _runtime.PackageStatus));
        info.Children.Add(InfoRow("Publisher", "Clipton"));
        info.Children.Add(InfoRow("Author", "Clipton contributors"));
        info.Children.Add(InfoRow("License", _runtime.Translate("LicenseValue")));
        _aboutPage.Children.Add(Card(info));

        var documents = new StackPanel { Spacing = 10 };
        documents.Children.Add(new TextBlock
        {
            Text = _runtime.Translate("LegalDescription"),
            Foreground = DescriptionBrush(),
            TextWrapping = TextWrapping.Wrap
        });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, Spacing = 8 };
        _termsButton.Click += (_, _) => ShowDocumentDialog(_runtime.Translate("TermsOfUse"), _runtime.Translate("TermsText"));
        _privacyButton.Click += (_, _) => ShowDocumentDialog(_runtime.Translate("PrivacyPolicy"), _runtime.Translate("PrivacyText"));
        buttons.Children.Add(_termsButton);
        buttons.Children.Add(_privacyButton);
        documents.Children.Add(buttons);
        _aboutPage.Children.Add(Card(documents));
    }

    private async Task SetStartupAsync()
    {
        if (_loading) return;
        await _runtime.SetStartWithWindowsAsync(_startupToggle.IsOn);
        RefreshTexts();
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
        _runtime.SetSimpleContextMenuMode(_simpleContextMenuToggle.IsOn);
        RefreshItems();
    }

    private void RegisterSelectedHistory()
    {
        if (_selectedHistoryId is null) return;
        var item = _runtime.History.Find(_selectedHistoryId);
        if (string.IsNullOrWhiteSpace(item?.Text)) return;
        SelectPage(2);
        OpenSnippetEditor(null, "History", CreateSnippetName(item.Text), item.Text);
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
            Icon = AppAssets.LoadAppIcon(),
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
        TextAlign = System.Drawing.ContentAlignment.MiddleLeft
    };

    private Forms.TextBox DialogTextBox(string text) => new()
    {
        Text = text,
        Dock = Forms.DockStyle.Fill,
        BackColor = IsDark ? System.Drawing.Color.FromArgb(43, 43, 43) : System.Drawing.Color.White,
        ForeColor = IsDark ? System.Drawing.Color.White : System.Drawing.Color.Black,
        BorderStyle = Forms.BorderStyle.FixedSingle
    };

    private string? PromptForText(string title, string message, string initialValue)
    {
        using var form = new Forms.Form
        {
            Text = title,
            Icon = AppAssets.LoadAppIcon(),
            Width = 440,
            Height = 170,
            MinimizeBox = false,
            MaximizeBox = false,
            FormBorderStyle = Forms.FormBorderStyle.FixedDialog,
            StartPosition = Forms.FormStartPosition.CenterScreen,
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
        layout.RowStyles.Add(new Forms.RowStyle(Forms.SizeType.Absolute, 32));
        layout.RowStyles.Add(new Forms.RowStyle(Forms.SizeType.Absolute, 34));
        layout.RowStyles.Add(new Forms.RowStyle(Forms.SizeType.Absolute, 44));

        var input = DialogTextBox(initialValue);
        var buttons = new Forms.FlowLayoutPanel
        {
            FlowDirection = Forms.FlowDirection.RightToLeft,
            Dock = Forms.DockStyle.Fill
        };
        var okButton = new Forms.Button { Text = _runtime.Translate("Search"), DialogResult = Forms.DialogResult.OK, Width = 96 };
        var cancelButton = new Forms.Button { Text = _runtime.Translate("Cancel"), DialogResult = Forms.DialogResult.Cancel, Width = 96 };
        buttons.Controls.Add(okButton);
        buttons.Controls.Add(cancelButton);

        layout.Controls.Add(DialogLabel(message), 0, 0);
        layout.Controls.Add(input, 0, 1);
        layout.Controls.Add(buttons, 0, 2);
        form.Controls.Add(layout);
        form.AcceptButton = okButton;
        form.CancelButton = cancelButton;
        form.Shown += (_, _) =>
        {
            ApplyFormTitleBarTheme(form);
            input.Focus();
            input.SelectAll();
        };

        return form.ShowDialog() == Forms.DialogResult.OK ? input.Text : null;
    }

    private string? PromptForHotkey()
    {
        _runtime.SuspendHotkey();
        using var form = new Forms.Form
        {
            Text = _runtime.Translate("CaptureHotkey"),
            Icon = AppAssets.LoadAppIcon(),
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
        _snippetPage.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
        _aboutPage.Visibility = index == 3 ? Visibility.Visible : Visibility.Collapsed;
        for (var i = 0; i < _navButtons.Count; i++)
        {
            _navButtons[i].Background = i == index ? AccentBrush(34) : Brush("#00FFFFFF");
            _navButtons[i].BorderBrush = i == index ? AccentBrush(68) : Brush("#00FFFFFF");
        }
    }

    private void ApplyTheme()
    {
        var dark = IsDark;
        _root.RequestedTheme = dark ? ElementTheme.Dark : ElementTheme.Light;
        _root.Background = Brush(dark ? "#202020" : "#F5F5F5");
        _sidebar.Background = Brush(dark ? "#171717" : "#F7F7F7");
        ApplyTitleBarTheme();
        foreach (var card in _cards)
        {
            card.Background = CardBackground();
            card.BorderBrush = CardBorderBrush();
        }
        SelectPage(_selectedPageIndex);
    }

    private bool IsDark => string.Equals(_runtime.EffectiveTheme, "dark", StringComparison.OrdinalIgnoreCase);

    private Border Card(UIElement child)
    {
        var card = new Border
        {
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

    private UIElement SettingCard(string glyph, string titleKey, string descriptionKey, FrameworkElement control)
    {
        if (control is Control controlElement)
        {
            controlElement.MinWidth = control is ToggleSwitch ? 0 : 220;
        }

        control.HorizontalAlignment = HorizontalAlignment.Right;

        var grid = new Grid { ColumnSpacing = 14, VerticalAlignment = VerticalAlignment.Center };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(IconCircle(glyph));

        var texts = new StackPanel { Spacing = 3 };
        texts.Children.Add(new TextBlock { Text = _runtime.Translate(titleKey), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        texts.Children.Add(new TextBlock { Text = _runtime.Translate(descriptionKey), FontSize = 12, Foreground = DescriptionBrush(), TextWrapping = TextWrapping.Wrap });
        Grid.SetColumn(texts, 1);
        grid.Children.Add(texts);
        Grid.SetColumn(control, 2);
        grid.Children.Add(control);
        return Card(grid);
    }

    private UIElement InfoRow(string labelKey, string value)
    {
        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(new TextBlock
        {
            Text = _runtime.Translate(labelKey),
            Foreground = DescriptionBrush(),
            TextWrapping = TextWrapping.Wrap
        });
        var valueText = new TextBlock
        {
            Text = value,
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetColumn(valueText, 1);
        grid.Children.Add(valueText);
        return grid;
    }

    private void ShowDocumentDialog(string title, string text)
    {
        using var form = new Forms.Form
        {
            Text = title,
            Icon = AppAssets.LoadAppIcon(),
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
                    new TextBlock { Text = subtitle, FontSize = 12, Foreground = DescriptionBrush() }
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

    private UIElement SectionHeader(string key) => new TextBlock
    {
        Text = _runtime.Translate(key),
        FontSize = 13,
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        Foreground = DescriptionBrush(),
        Margin = new Thickness(1, 6, 0, -8)
    };

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
        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
        if (File.Exists(AppAssets.AppIconPath))
        {
            appWindow.SetIcon(AppAssets.AppIconPath);
        }

        appWindow.Resize(new SizeInt32(1120, 760));
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

    private static ToggleSwitch CompactToggle() => new()
    {
        MinWidth = 72,
        OnContent = string.Empty,
        OffContent = string.Empty
    };

    private static Image CreateAppLogoImage(double size) => new()
    {
        Width = size,
        Height = size,
        Source = File.Exists(AppAssets.AppImagePath)
            ? new BitmapImage(new Uri(AppAssets.AppImagePath))
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
