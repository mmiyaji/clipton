using System.ComponentModel;
using System.Windows.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Clipton.Core;

namespace Clipton.App;

public sealed partial class MainWindow : Window
{
    private readonly CliptonRuntime _runtime;
    private bool _loading;

    public MainWindow(CliptonRuntime runtime)
    {
        _runtime = runtime;
        InitializeComponent();
        ApplyTheme();
        NavList.SelectedIndex = 0;
        RefreshTexts();
        RefreshItems();
    }

    public void RefreshItems()
    {
        HistoryList.ItemsSource = _runtime.History.Items.Select(_runtime.CreateHistoryItemViewModel).ToArray();
        SnippetList.ItemsSource = _runtime.Snippets.Snippets
            .OrderBy(snippet => snippet.Folder, StringComparer.OrdinalIgnoreCase)
            .ThenBy(snippet => snippet.Name, StringComparer.OrdinalIgnoreCase)
            .Select(SnippetItemViewModel.FromSnippet)
            .ToArray();
    }

    public void RefreshTexts()
    {
        _loading = true;
        var t = _runtime.Translate;
        TitleText.Text = t("AppName");
        GeneralNavItem.Content = t("General");
        HistoryNavItem.Content = t("History");
        SnippetNavItem.Content = t("Snippets");
        GeneralHeaderText.Text = t("General");
        GeneralDescriptionText.Text = t("GeneralDescription");
        HistoryHeaderText.Text = t("History");
        HistoryDescriptionText.Text = t("HistoryDescription");
        SnippetHeaderText.Text = t("Snippets");
        SnippetDescriptionText.Text = t("SnippetDescription");
        SnippetFolderTitleText.Text = t("SnippetFolder");
        HotkeyTitleText.Text = t("Hotkey");
        HotkeyDescriptionText.Text = t("HotkeyDescription");
        LanguageTitleText.Text = t("Language");
        LanguageDescriptionText.Text = t("LanguageDescription");
        ThemeTitleText.Text = t("Theme");
        ThemeDescriptionText.Text = t("ThemeDescription");
        StartupTitleText.Text = t("Startup");
        StartupDescriptionText.Text = t("StartupDescription");
        SnippetNameTitleText.Text = t("SnippetName");
        SnippetTextTitleText.Text = t("SnippetText");
        RegisterFromHistoryButton.Content = t("RegisterFromHistory");
        ClearButton.Content = t("ClearHistory");
        SaveSnippetButton.Content = t("Save");
        DeleteSnippetButton.Content = t("Delete");
        StartupCheckBox.Content = t("Startup");
        PauseCaptureCheckBox.Content = t("PauseCapture");
        PersistHistoryCheckBox.Content = t("PersistHistory");
        MaskSensitiveContentCheckBox.Content = t("MaskSensitiveContent");
        FolderModeCheckBox.Content = t("FolderMode");
        SimpleContextMenuCheckBox.Content = t("SimpleContextMenuMode");
        StartupCheckBox.IsChecked = _runtime.Settings.StartWithWindows;
        PauseCaptureCheckBox.IsChecked = _runtime.Settings.PauseCapture;
        PersistHistoryCheckBox.IsChecked = _runtime.Settings.PersistEncryptedHistory;
        MaskSensitiveContentCheckBox.IsChecked = _runtime.Settings.MaskSensitiveContent;
        FolderModeCheckBox.IsChecked = _runtime.Settings.FolderMode;
        SimpleContextMenuCheckBox.IsChecked = _runtime.Settings.SimpleContextMenuMode;
        HotkeyBox.Text = _runtime.Settings.Hotkey;
        HotkeyText.Text = $"{t("Hotkey")}: {_runtime.Settings.Hotkey}";
        SetComboBoxText(LocaleBox, "system", t("LanguageSystem"));
        foreach (var supportedLocale in LocalizationCatalog.SupportedLocales)
        {
            SetComboBoxText(LocaleBox, supportedLocale.Code, t(supportedLocale.DisplayNameKey));
        }

        SetComboBoxText(ThemeBox, "light", t("ThemeLight"));
        SetComboBoxText(ThemeBox, "dark", t("ThemeDark"));

        foreach (ComboBoxItem item in LocaleBox.Items)
        {
            if (Equals(item.Tag, _runtime.Settings.Locale))
            {
                LocaleBox.SelectedItem = item;
                break;
            }
        }

        foreach (ComboBoxItem item in ThemeBox.Items)
        {
            if (Equals(item.Tag, _runtime.Settings.Theme))
            {
                ThemeBox.SelectedItem = item;
                break;
            }
        }

        _loading = false;
    }

    public void OpenNewSnippetEditor()
    {
        SnippetFolderBox.Clear();
        SnippetNameBox.Clear();
        SnippetTextBox.Clear();
        NavList.SelectedItem = SnippetNavItem;
        SnippetNameBox.Focus();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }

    private void HistoryList_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (HistoryList.SelectedItem is HistoryItemViewModel item)
        {
            _runtime.PasteHistoryItem(item.Id, asPlainText: false);
        }
    }

    private void SnippetList_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SnippetList.SelectedItem is SnippetItemViewModel item)
        {
            _runtime.PasteSnippet(item.Folder, item.Name);
        }
    }

    private void SnippetList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SnippetList.SelectedItem is not SnippetItemViewModel selected)
        {
            return;
        }

        var snippet = _runtime.Snippets.Find(selected.Folder, selected.Name);
        if (snippet is null)
        {
            return;
        }

        SnippetFolderBox.Text = snippet.Folder;
        SnippetNameBox.Text = snippet.Name;
        SnippetTextBox.Text = snippet.Text;
    }

    private void ClearButton_OnClick(object sender, RoutedEventArgs e)
    {
        _runtime.ClearHistory();
    }

    private void RegisterFromHistoryButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (HistoryList.SelectedItem is not HistoryItemViewModel selected)
        {
            return;
        }

        var item = _runtime.History.Find(selected.Id);
        if (string.IsNullOrWhiteSpace(item?.Text))
        {
            return;
        }

        SnippetFolderBox.Clear();
        SnippetNameBox.Text = CreateSnippetName(item.Text);
        SnippetTextBox.Text = item.Text;
        NavList.SelectedItem = SnippetNavItem;
        SnippetNameBox.Focus();
        SnippetNameBox.SelectAll();
    }

    private async void StartupCheckBox_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        await _runtime.SetStartWithWindowsAsync(StartupCheckBox.IsChecked == true);
        RefreshTexts();
    }

    private void PauseCaptureCheckBox_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _runtime.SetPauseCapture(PauseCaptureCheckBox.IsChecked == true);
    }

    private void PersistHistoryCheckBox_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _runtime.SetPersistEncryptedHistory(PersistHistoryCheckBox.IsChecked == true);
    }

    private void MaskSensitiveContentCheckBox_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _runtime.SetMaskSensitiveContent(MaskSensitiveContentCheckBox.IsChecked == true);
        RefreshItems();
    }

    private void FolderModeCheckBox_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _runtime.SetFolderMode(FolderModeCheckBox.IsChecked == true);
    }

    private void SimpleContextMenuCheckBox_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _runtime.SetSimpleContextMenuMode(SimpleContextMenuCheckBox.IsChecked == true);
    }

    private void HistoryList_OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Delete && HistoryList.SelectedItem is HistoryItemViewModel item)
        {
            _runtime.RemoveHistoryItem(item.Id);
            e.Handled = true;
        }
    }

    private void HotkeyBox_OnLostFocus(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _runtime.SetHotkey(HotkeyBox.Text);
        RefreshTexts();
    }

    private void LocaleBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || LocaleBox.SelectedItem is not ComboBoxItem item || item.Tag is not string locale)
        {
            return;
        }

        _runtime.SetLocale(locale);
        RefreshTexts();
    }

    private void ThemeBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ThemeBox.SelectedItem is not ComboBoxItem item || item.Tag is not string theme)
        {
            return;
        }

        _runtime.SetTheme(theme);
        ApplyTheme();
    }

    private void SaveSnippetButton_OnClick(object sender, RoutedEventArgs e)
    {
        _runtime.UpsertSnippet(SnippetFolderBox.Text, SnippetNameBox.Text, SnippetTextBox.Text);
        RefreshItems();
    }

    private void DeleteSnippetButton_OnClick(object sender, RoutedEventArgs e)
    {
        _runtime.RemoveSnippet(SnippetFolderBox.Text, SnippetNameBox.Text);
        SnippetFolderBox.Clear();
        SnippetNameBox.Clear();
        SnippetTextBox.Clear();
        RefreshItems();
    }

    private void NavList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GeneralPage is null)
        {
            return;
        }

        GeneralPage.Visibility = NavList.SelectedItem == GeneralNavItem ? Visibility.Visible : Visibility.Collapsed;
        HistoryPage.Visibility = NavList.SelectedItem == HistoryNavItem ? Visibility.Visible : Visibility.Collapsed;
        SnippetPage.Visibility = NavList.SelectedItem == SnippetNavItem ? Visibility.Visible : Visibility.Collapsed;
    }
}

public sealed record HistoryItemViewModel(string Id, string Preview, string FormatSummary)
{
}

public sealed record SnippetItemViewModel(string Folder, string Name, string DisplayName)
{
    public static SnippetItemViewModel FromSnippet(Snippet snippet)
    {
        return new SnippetItemViewModel(snippet.Folder, snippet.Name, snippet.DisplayName);
    }

    public override string ToString() => DisplayName;
}

public sealed partial class MainWindow
{
    private void ApplyTheme()
    {
        var dark = string.Equals(_runtime.Settings.Theme, "dark", StringComparison.OrdinalIgnoreCase);
        WindowThemeService.Apply(this, _runtime.Settings.Theme);

        if (dark)
        {
            SetBrush("PageBrush", "#101418");
            SetBrush("SidebarBrush", "#151A20");
            SetBrush("PanelBrush", "#1B222A");
            SetBrush("InputBrush", "#111820");
            SetBrush("BorderBrushSoft", "#2E3742");
            SetBrush("BorderBrushStrong", "#46515E");
            SetBrush("TextPrimaryBrush", "#EEF2F6");
            SetBrush("TextSecondaryBrush", "#A7B0BA");
            SetBrush("AccentBrush", "#4EA1FF");
            SetBrush("AccentSoftBrush", "#18324C");
            SetBrush("AccentTextBrush", "#CDE5FF");
            return;
        }

        SetBrush("PageBrush", "#F4F6F8");
        SetBrush("SidebarBrush", "#FAFBFC");
        SetBrush("PanelBrush", "#FFFFFF");
        SetBrush("InputBrush", "#FFFFFF");
        SetBrush("BorderBrushSoft", "#D8DEE6");
        SetBrush("BorderBrushStrong", "#C3CAD4");
        SetBrush("TextPrimaryBrush", "#17212B");
        SetBrush("TextSecondaryBrush", "#667085");
        SetBrush("AccentBrush", "#005FB8");
        SetBrush("AccentSoftBrush", "#EAF3FF");
        SetBrush("AccentTextBrush", "#003E73");
    }

    private void SetBrush(string key, string color)
    {
        Resources[key] = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
    }

    private static void SetComboBoxText(System.Windows.Controls.ComboBox comboBox, string tag, string content)
    {
        foreach (ComboBoxItem item in comboBox.Items)
        {
            if (Equals(item.Tag, tag))
            {
                item.Content = content;
                return;
            }
        }

        comboBox.Items.Add(new ComboBoxItem { Tag = tag, Content = content });
    }

    private static string CreateSnippetName(string text)
    {
        var normalized = string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= 32 ? normalized : normalized[..32];
    }
}
