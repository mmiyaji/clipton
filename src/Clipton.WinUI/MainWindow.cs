using Clipton.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Clipton.WinUI;

public sealed class MainWindow : Window
{
    private readonly CliptonRuntime _runtime;
    private readonly Grid _root = new();
    private readonly StackPanel _sidebar = new() { Padding = new Thickness(18), Spacing = 10 };
    private readonly StackPanel _generalPage = new() { Spacing = 12 };
    private readonly StackPanel _historyPage = new() { Spacing = 12 };
    private readonly StackPanel _snippetPage = new() { Spacing = 12 };
    private readonly StackPanel _historyItemsPanel = new() { Spacing = 6 };
    private readonly StackPanel _snippetItemsPanel = new() { Spacing = 6 };
    private readonly List<Border> _cards = [];
    private readonly Button _generalNavButton = new();
    private readonly Button _historyNavButton = new();
    private readonly Button _snippetNavButton = new();
    private readonly TextBlock _titleText = Header(22);
    private readonly TextBlock _hotkeyText = Description();
    private readonly TextBlock _generalHeaderText = Header();
    private readonly TextBlock _generalDescriptionText = Description();
    private readonly TextBlock _historyHeaderText = Header();
    private readonly TextBlock _historyDescriptionText = Description();
    private readonly TextBlock _snippetHeaderText = Header();
    private readonly TextBlock _snippetDescriptionText = Description();
    private readonly ComboBox _hotkeyBox = new();
    private readonly ComboBox _themeBox = new();
    private readonly ComboBox _localeBox = new();
    private readonly CheckBox _startupCheckBox = new();
    private readonly CheckBox _pauseCaptureCheckBox = new();
    private readonly CheckBox _persistHistoryCheckBox = new();
    private readonly CheckBox _maskSensitiveContentCheckBox = new();
    private readonly CheckBox _folderModeCheckBox = new();
    private readonly CheckBox _simpleContextMenuCheckBox = new();
    private readonly Button _registerFromHistoryButton = new();
    private readonly Button _clearButton = new();
    private readonly TextBlock _selectedSnippetText = Description();
    private readonly Button _pasteSnippetButton = new();
    private readonly Button _deleteSnippetButton = new();
    private string? _selectedHistoryId;
    private SnippetItemViewModel? _selectedSnippet;
    private bool _loading;

    public MainWindow(CliptonRuntime runtime)
    {
        _runtime = runtime;
        Title = "Clipton";
        BuildUi();
        ApplyTheme();
        RefreshTexts();
        RefreshItems();
        Closed += (_, _) => _runtime.OnMainWindowClosed(this);
    }

    public void RefreshItems()
    {
        _historyItemsPanel.Children.Clear();
        foreach (var item in _runtime.History.Items.Select(_runtime.CreateHistoryItemViewModel))
        {
            var button = ItemButton(item.Preview, item.FormatSummary);
            button.Click += (_, _) => _selectedHistoryId = item.Id;
            button.DoubleTapped += (_, _) => _runtime.PasteHistoryItem(item.Id, asPlainText: false);
            _historyItemsPanel.Children.Add(button);
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
        _generalHeaderText.Text = t("General");
        _generalDescriptionText.Text = t("GeneralDescription");
        _historyHeaderText.Text = t("History");
        _historyDescriptionText.Text = t("HistoryDescription");
        _snippetHeaderText.Text = t("Snippets");
        _snippetDescriptionText.Text = t("SnippetDescription");
        _startupCheckBox.Content = t("Startup");
        _pauseCaptureCheckBox.Content = t("PauseCapture");
        _persistHistoryCheckBox.Content = t("PersistHistory");
        _maskSensitiveContentCheckBox.Content = t("MaskSensitiveContent");
        _folderModeCheckBox.Content = t("FolderMode");
        _simpleContextMenuCheckBox.Content = t("SimpleContextMenuMode");
        _registerFromHistoryButton.Content = t("RegisterFromHistory");
        _clearButton.Content = t("ClearHistory");
        _pasteSnippetButton.Content = t("Paste");
        _deleteSnippetButton.Content = t("Delete");
        SetComboBoxText(_themeBox, "light", t("ThemeLight"));
        SetComboBoxText(_themeBox, "dark", t("ThemeDark"));
        _startupCheckBox.IsChecked = _runtime.Settings.StartWithWindows;
        _pauseCaptureCheckBox.IsChecked = _runtime.Settings.PauseCapture;
        _persistHistoryCheckBox.IsChecked = _runtime.Settings.PersistEncryptedHistory;
        _maskSensitiveContentCheckBox.IsChecked = _runtime.Settings.MaskSensitiveContent;
        _folderModeCheckBox.IsChecked = _runtime.Settings.FolderMode;
        _simpleContextMenuCheckBox.IsChecked = _runtime.Settings.SimpleContextMenuMode;
        SetComboSelection(_hotkeyBox, _runtime.Settings.Hotkey);
        SetComboSelection(_themeBox, _runtime.Settings.Theme);
        SetComboSelection(_localeBox, _runtime.Settings.Locale);
        UpdateSelectedSnippetText();
        _loading = false;
    }

    private void BuildUi()
    {
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        _root.ColumnDefinitions.Add(new ColumnDefinition());
        Content = _root;

        Grid.SetColumn(_sidebar, 0);
        _sidebar.Children.Add(_titleText);
        _sidebar.Children.Add(_hotkeyText);
        _generalNavButton.Click += (_, _) => SelectPage(0);
        _historyNavButton.Click += (_, _) => SelectPage(1);
        _snippetNavButton.Click += (_, _) => SelectPage(2);
        _sidebar.Children.Add(_generalNavButton);
        _sidebar.Children.Add(_historyNavButton);
        _sidebar.Children.Add(_snippetNavButton);
        _root.Children.Add(_sidebar);

        var scroller = new ScrollViewer { Padding = new Thickness(28), VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetColumn(scroller, 1);
        var contentHost = new Grid();
        scroller.Content = contentHost;
        contentHost.Children.Add(_generalPage);
        contentHost.Children.Add(_historyPage);
        contentHost.Children.Add(_snippetPage);
        _root.Children.Add(scroller);

        BuildGeneralPage();
        BuildHistoryPage();
        BuildSnippetPage();
        SelectPage(0);
    }

    private void BuildGeneralPage()
    {
        _generalPage.Children.Add(_generalHeaderText);
        _generalPage.Children.Add(_generalDescriptionText);
        foreach (var hotkey in new[] { "Ctrl+Shift+V", "Ctrl+Alt+V", "Alt+Space" })
        {
            _hotkeyBox.Items.Add(new ComboBoxItem { Content = hotkey, Tag = hotkey });
        }

        _hotkeyBox.SelectionChanged += (_, _) =>
        {
            if (_loading || (_hotkeyBox.SelectedItem as ComboBoxItem)?.Tag is not string hotkey) return;
            _runtime.SetHotkey(hotkey);
            RefreshTexts();
        };
        _generalPage.Children.Add(SettingRow("Hotkey", "HotkeyDescription", _hotkeyBox));

        _themeBox.Items.Add(new ComboBoxItem { Tag = "light" });
        _themeBox.Items.Add(new ComboBoxItem { Tag = "dark" });
        _themeBox.SelectionChanged += (_, _) =>
        {
            if (_loading || (_themeBox.SelectedItem as ComboBoxItem)?.Tag is not string theme) return;
            _runtime.SetTheme(theme);
            ApplyTheme();
        };
        _generalPage.Children.Add(SettingRow("Theme", "ThemeDescription", _themeBox));

        _localeBox.Items.Add(new ComboBoxItem { Content = "English", Tag = "en" });
        _localeBox.Items.Add(new ComboBoxItem { Content = "Japanese", Tag = "ja" });
        _localeBox.SelectionChanged += (_, _) =>
        {
            if (_loading || (_localeBox.SelectedItem as ComboBoxItem)?.Tag is not string locale) return;
            _runtime.SetLocale(locale);
            RefreshTexts();
        };
        _generalPage.Children.Add(SettingRow("Language", "LanguageDescription", _localeBox));

        _startupCheckBox.Checked += async (_, _) => await SetStartupAsync();
        _startupCheckBox.Unchecked += async (_, _) => await SetStartupAsync();
        _generalPage.Children.Add(Card(_startupCheckBox));
    }

    private void BuildHistoryPage()
    {
        _historyPage.Children.Add(_historyHeaderText);
        _historyPage.Children.Add(_historyDescriptionText);
        foreach (var checkBox in new[] { _pauseCaptureCheckBox, _persistHistoryCheckBox, _maskSensitiveContentCheckBox, _folderModeCheckBox, _simpleContextMenuCheckBox })
        {
            checkBox.Checked += (_, _) => SaveHistoryOptions();
            checkBox.Unchecked += (_, _) => SaveHistoryOptions();
        }

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        _registerFromHistoryButton.Click += (_, _) => RegisterSelectedHistory();
        _clearButton.Click += (_, _) => _runtime.ClearHistory();
        actions.Children.Add(_registerFromHistoryButton);
        actions.Children.Add(_clearButton);
        _historyPage.Children.Add(Card(new StackPanel
        {
            Spacing = 8,
            Children =
            {
                _pauseCaptureCheckBox,
                _persistHistoryCheckBox,
                _maskSensitiveContentCheckBox,
                _folderModeCheckBox,
                _simpleContextMenuCheckBox,
                actions
            }
        }));
        _historyPage.Children.Add(Card(_historyItemsPanel));
    }

    private void BuildSnippetPage()
    {
        _snippetPage.Children.Add(_snippetHeaderText);
        _snippetPage.Children.Add(_snippetDescriptionText);
        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(Card(_snippetItemsPanel));

        var details = new StackPanel { Spacing = 10 };
        details.Children.Add(_selectedSnippetText);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
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
        buttons.Children.Add(_pasteSnippetButton);
        buttons.Children.Add(_deleteSnippetButton);
        details.Children.Add(buttons);
        var detailsCard = Card(details);
        Grid.SetColumn(detailsCard, 1);
        grid.Children.Add(detailsCard);
        _snippetPage.Children.Add(grid);
    }

    private async Task SetStartupAsync()
    {
        if (_loading) return;
        await _runtime.SetStartWithWindowsAsync(_startupCheckBox.IsChecked == true);
        RefreshTexts();
    }

    private void SaveHistoryOptions()
    {
        if (_loading) return;
        _runtime.SetPauseCapture(_pauseCaptureCheckBox.IsChecked == true);
        _runtime.SetPersistEncryptedHistory(_persistHistoryCheckBox.IsChecked == true);
        _runtime.SetMaskSensitiveContent(_maskSensitiveContentCheckBox.IsChecked == true);
        _runtime.SetFolderMode(_folderModeCheckBox.IsChecked == true);
        _runtime.SetSimpleContextMenuMode(_simpleContextMenuCheckBox.IsChecked == true);
        RefreshItems();
    }

    private void RegisterSelectedHistory()
    {
        if (_selectedHistoryId is null) return;
        var item = _runtime.History.Find(_selectedHistoryId);
        if (string.IsNullOrWhiteSpace(item?.Text)) return;
        _runtime.UpsertSnippet("History", CreateSnippetName(item.Text), item.Text);
        SelectPage(2);
    }

    private void SelectSnippet(SnippetItemViewModel selected)
    {
        _selectedSnippet = selected;
        UpdateSelectedSnippetText();
    }

    private void UpdateSelectedSnippetText()
    {
        _selectedSnippetText.Text = _selectedSnippet is null
            ? _runtime.Translate("Snippets")
            : $"{_selectedSnippet.DisplayName}\n{_selectedSnippet.Folder}";
    }

    private void SelectPage(int index)
    {
        _generalPage.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
        _historyPage.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
        _snippetPage.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyTheme()
    {
        var dark = IsDark;
        _root.RequestedTheme = dark ? ElementTheme.Dark : ElementTheme.Light;
        _root.Background = Brush(dark ? "#202020" : "#F3F3F3");
        _sidebar.Background = Brush(dark ? "#1B1B1B" : "#F9F9F9");
        foreach (var card in _cards)
        {
            card.Background = CardBackground();
            card.BorderBrush = CardBorderBrush();
        }
    }

    private bool IsDark => string.Equals(_runtime.Settings.Theme, "dark", StringComparison.OrdinalIgnoreCase);

    private Border Card(UIElement child)
    {
        var card = new Border
        {
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = CardBorderBrush(),
            Background = CardBackground(),
            Child = child
        };
        _cards.Add(card);
        return card;
    }

    private UIElement SettingRow(string titleKey, string descriptionKey, Control control)
    {
        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        var texts = new StackPanel { Spacing = 3 };
        texts.Children.Add(new TextBlock { Text = _runtime.Translate(titleKey), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        texts.Children.Add(new TextBlock { Text = _runtime.Translate(descriptionKey), FontSize = 12, Foreground = DescriptionBrush(), TextWrapping = TextWrapping.Wrap });
        grid.Children.Add(texts);
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        return Card(grid);
    }

    private Button ItemButton(string title, string subtitle)
    {
        return new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Content = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = title, TextTrimming = TextTrimming.CharacterEllipsis },
                    new TextBlock { Text = subtitle, FontSize = 12, Foreground = DescriptionBrush() }
                }
            }
        };
    }

    private Brush CardBackground() => Brush(IsDark ? "#2B2B2B" : "#FFFFFF");

    private Brush CardBorderBrush() => Brush(IsDark ? "#3F3F3F" : "#E5E5E5");

    private Brush DescriptionBrush() => Brush(IsDark ? "#C7C7C7" : "#667085");

    private static SolidColorBrush Brush(string color)
    {
        return new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(
            255,
            Convert.ToByte(color.Substring(1, 2), 16),
            Convert.ToByte(color.Substring(3, 2), 16),
            Convert.ToByte(color.Substring(5, 2), 16)));
    }

    private static TextBlock Header(double size = 28) => new() { FontSize = size, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };

    private static TextBlock Description() => new() { Foreground = Brush("#667085"), TextWrapping = TextWrapping.Wrap };

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
