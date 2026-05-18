using Clipton.Core;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WinRT.Interop;

namespace Clipton.WinUI;

public sealed class MainWindow : Window
{
    private readonly CliptonRuntime _runtime;
    private readonly Grid _root = new();
    private readonly ListView _navList = new();
    private readonly StackPanel _generalPage = new() { Spacing = 12 };
    private readonly StackPanel _historyPage = new() { Spacing = 12 };
    private readonly StackPanel _snippetPage = new() { Spacing = 12 };
    private readonly List<Border> _cards = [];
    private readonly TextBlock _titleText = new() { FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
    private readonly TextBlock _hotkeyText = new() { FontSize = 12 };
    private readonly TextBlock _generalHeaderText = Header();
    private readonly TextBlock _generalDescriptionText = Description();
    private readonly TextBlock _historyHeaderText = Header();
    private readonly TextBlock _historyDescriptionText = Description();
    private readonly TextBlock _snippetHeaderText = Header();
    private readonly TextBlock _snippetDescriptionText = Description();
    private readonly TextBox _hotkeyBox = new();
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
    private readonly ListView _historyList = new();
    private readonly ListView _snippetList = new();
    private readonly TextBox _snippetFolderBox = new();
    private readonly TextBox _snippetNameBox = new();
    private readonly TextBox _snippetTextBox = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 96 };
    private readonly Button _saveSnippetButton = new();
    private readonly Button _deleteSnippetButton = new();
    private StackPanel? _sidebar;
    private bool _loading;

    public MainWindow(CliptonRuntime runtime)
    {
        _runtime = runtime;
        Title = "Clipton";
        SystemBackdrop = new MicaBackdrop();
        BuildUi();
        ApplyTheme();
        RefreshTexts();
        RefreshItems();
        SetWindowIconAndSize();
    }

    public void RefreshItems()
    {
        _historyList.ItemsSource = _runtime.History.Items.Select(_runtime.CreateHistoryItemViewModel).ToArray();
        _snippetList.ItemsSource = _runtime.Snippets.Snippets
            .OrderBy(snippet => snippet.Folder, StringComparer.OrdinalIgnoreCase)
            .ThenBy(snippet => snippet.Name, StringComparer.OrdinalIgnoreCase)
            .Select(SnippetItemViewModel.FromSnippet)
            .ToArray();
    }

    public void RefreshTexts()
    {
        _loading = true;
        var t = _runtime.Translate;
        _titleText.Text = t("AppName");
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
        _saveSnippetButton.Content = t("Save");
        _deleteSnippetButton.Content = t("Delete");
        _hotkeyText.Text = $"{t("Hotkey")}: {_runtime.Settings.Hotkey}";
        _hotkeyBox.Text = _runtime.Settings.Hotkey;
        _startupCheckBox.IsChecked = _runtime.Settings.StartWithWindows;
        _pauseCaptureCheckBox.IsChecked = _runtime.Settings.PauseCapture;
        _persistHistoryCheckBox.IsChecked = _runtime.Settings.PersistEncryptedHistory;
        _maskSensitiveContentCheckBox.IsChecked = _runtime.Settings.MaskSensitiveContent;
        _folderModeCheckBox.IsChecked = _runtime.Settings.FolderMode;
        _simpleContextMenuCheckBox.IsChecked = _runtime.Settings.SimpleContextMenuMode;
        SetComboSelection(_themeBox, _runtime.Settings.Theme);
        SetComboSelection(_localeBox, _runtime.Settings.Locale);
        _loading = false;
    }

    private void BuildUi()
    {
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        _root.ColumnDefinitions.Add(new ColumnDefinition());
        Content = _root;

        _sidebar = new StackPanel { Padding = new Thickness(18), Spacing = 12 };
        Grid.SetColumn(_sidebar, 0);
        _titleText.FontFamily = new FontFamily("Segoe UI Variable Display");
        _hotkeyText.Foreground = Brush("#667085");
        _sidebar.Children.Add(_titleText);
        _sidebar.Children.Add(_hotkeyText);
        _navList.ItemsSource = new[] { "General", "History", "Snippets" };
        _navList.SelectedIndex = 0;
        _navList.SelectionChanged += (_, _) => SelectPage(_navList.SelectedIndex);
        _sidebar.Children.Add(_navList);
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
        _generalPage.Children.Add(SettingRow(_runtime.Translate("Hotkey"), _runtime.Translate("HotkeyDescription"), _hotkeyBox));
        _hotkeyBox.LostFocus += (_, _) =>
        {
            if (_loading) return;
            _runtime.SetHotkey(_hotkeyBox.Text);
            RefreshTexts();
        };

        _themeBox.Items.Add(new ComboBoxItem { Content = _runtime.Translate("ThemeLight"), Tag = "light" });
        _themeBox.Items.Add(new ComboBoxItem { Content = _runtime.Translate("ThemeDark"), Tag = "dark" });
        _themeBox.SelectionChanged += (_, _) =>
        {
            if (_loading || (_themeBox.SelectedItem as ComboBoxItem)?.Tag is not string theme) return;
            _runtime.SetTheme(theme);
            ApplyTheme();
        };
        _generalPage.Children.Add(SettingRow(_runtime.Translate("Theme"), _runtime.Translate("ThemeDescription"), _themeBox));

        _localeBox.Items.Add(new ComboBoxItem { Content = "English", Tag = "en" });
        _localeBox.Items.Add(new ComboBoxItem { Content = "Japanese", Tag = "ja" });
        _localeBox.SelectionChanged += (_, _) =>
        {
            if (_loading || (_localeBox.SelectedItem as ComboBoxItem)?.Tag is not string locale) return;
            _runtime.SetLocale(locale);
            RefreshTexts();
        };
        _generalPage.Children.Add(SettingRow(_runtime.Translate("Language"), _runtime.Translate("LanguageDescription"), _localeBox));
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
        _historyList.MinHeight = 300;
        _historyList.DoubleTapped += (_, _) =>
        {
            if (_historyList.SelectedItem is HistoryItemViewModel item)
            {
                _runtime.PasteHistoryItem(item.Id, asPlainText: false);
            }
        };
        _historyPage.Children.Add(ListPanel(_historyList));
    }

    private void BuildSnippetPage()
    {
        _snippetPage.Children.Add(_snippetHeaderText);
        _snippetPage.Children.Add(_snippetDescriptionText);
        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        _snippetList.SelectionChanged += (_, _) =>
        {
            if (_snippetList.SelectedItem is not SnippetItemViewModel selected) return;
            var snippet = _runtime.Snippets.Find(selected.Folder, selected.Name);
            if (snippet is null) return;
            _snippetFolderBox.Text = snippet.Folder;
            _snippetNameBox.Text = snippet.Name;
            _snippetTextBox.Text = snippet.Text;
        };
        _snippetList.DoubleTapped += (_, _) =>
        {
            if (_snippetList.SelectedItem is SnippetItemViewModel item)
            {
                _runtime.PasteSnippet(item.Folder, item.Name);
            }
        };
        grid.Children.Add(ListPanel(_snippetList));
        var editor = new StackPanel { Spacing = 8 };
        Grid.SetColumn(editor, 1);
        editor.Children.Add(new TextBlock { Text = _runtime.Translate("SnippetFolder") });
        editor.Children.Add(_snippetFolderBox);
        editor.Children.Add(new TextBlock { Text = _runtime.Translate("SnippetName") });
        editor.Children.Add(_snippetNameBox);
        editor.Children.Add(new TextBlock { Text = _runtime.Translate("SnippetText") });
        editor.Children.Add(_snippetTextBox);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        _saveSnippetButton.Click += (_, _) =>
        {
            _runtime.UpsertSnippet(_snippetFolderBox.Text, _snippetNameBox.Text, _snippetTextBox.Text);
            RefreshItems();
        };
        _deleteSnippetButton.Click += (_, _) =>
        {
            _runtime.RemoveSnippet(_snippetFolderBox.Text, _snippetNameBox.Text);
            _snippetFolderBox.Text = "";
            _snippetNameBox.Text = "";
            _snippetTextBox.Text = "";
            RefreshItems();
        };
        buttons.Children.Add(_saveSnippetButton);
        buttons.Children.Add(_deleteSnippetButton);
        editor.Children.Add(buttons);
        var editorCard = Card(editor);
        Grid.SetColumn(editorCard, 1);
        grid.Children.Add(editorCard);
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
        if (_historyList.SelectedItem is not HistoryItemViewModel selected) return;
        var item = _runtime.History.Find(selected.Id);
        if (string.IsNullOrWhiteSpace(item?.Text)) return;
        _snippetFolderBox.Text = "";
        _snippetNameBox.Text = CreateSnippetName(item.Text);
        _snippetTextBox.Text = item.Text;
        _navList.SelectedIndex = 2;
        SelectPage(2);
        _snippetNameBox.Focus(FocusState.Programmatic);
        _snippetNameBox.SelectAll();
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
        if (_sidebar is not null)
        {
            _sidebar.Background = Brush(dark ? "#1B1B1B" : "#F9F9F9");
        }

        _hotkeyText.Foreground = Brush(dark ? "#C7C7C7" : "#667085");
        _generalDescriptionText.Foreground = DescriptionBrush();
        _historyDescriptionText.Foreground = DescriptionBrush();
        _snippetDescriptionText.Foreground = DescriptionBrush();

        foreach (var card in _cards)
        {
            card.Background = CardBackground();
            card.BorderBrush = CardBorderBrush();
        }
    }

    private void SetWindowIconAndSize()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var id = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(id);
        appWindow.Resize(new Windows.Graphics.SizeInt32(980, 640));
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Clipton.ico");
        if (File.Exists(iconPath))
        {
            appWindow.SetIcon(iconPath);
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

    private Border ListPanel(ListView listView)
    {
        listView.BorderThickness = new Thickness(0);
        listView.Background = new SolidColorBrush(Colors.Transparent);
        return Card(listView);
    }

    private UIElement SettingRow(string title, string description, Control control)
    {
        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        var texts = new StackPanel { Spacing = 3 };
        texts.Children.Add(new TextBlock { Text = title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        texts.Children.Add(new TextBlock { Text = description, FontSize = 12, Foreground = DescriptionBrush(), TextWrapping = TextWrapping.Wrap });
        grid.Children.Add(texts);
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        return Card(grid);
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

    private static TextBlock Header() => new() { FontSize = 28, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };

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
