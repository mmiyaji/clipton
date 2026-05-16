using System.ComponentModel;
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
        RefreshTexts();
        RefreshItems();
    }

    public void RefreshItems()
    {
        HistoryList.ItemsSource = _runtime.History.Items.Select(HistoryItemViewModel.FromSnapshot).ToArray();
        SnippetList.ItemsSource = _runtime.Snippets.Snippets.Select(snippet => snippet.Name).ToArray();
    }

    public void RefreshTexts()
    {
        _loading = true;
        var t = _runtime.Translate;
        TitleText.Text = t("AppName");
        HistoryTab.Header = t("History");
        SnippetTab.Header = t("Snippets");
        ClearButton.Content = t("ClearHistory");
        StartupCheckBox.Content = t("Startup");
        PauseCaptureCheckBox.Content = t("PauseCapture");
        PersistHistoryCheckBox.Content = t("PersistHistory");
        StartupCheckBox.IsChecked = _runtime.Settings.StartWithWindows;
        PauseCaptureCheckBox.IsChecked = _runtime.Settings.PauseCapture;
        PersistHistoryCheckBox.IsChecked = _runtime.Settings.PersistEncryptedHistory;
        HotkeyBox.Text = _runtime.Settings.Hotkey;
        HotkeyText.Text = $"{t("Hotkey")}: {_runtime.Settings.Hotkey}";

        foreach (ComboBoxItem item in LocaleBox.Items)
        {
            if (Equals(item.Tag, _runtime.Settings.Locale))
            {
                LocaleBox.SelectedItem = item;
                break;
            }
        }

        _loading = false;
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
        if (SnippetList.SelectedItem is string name)
        {
            _runtime.PasteSnippet(name);
        }
    }

    private void ClearButton_OnClick(object sender, RoutedEventArgs e)
    {
        _runtime.ClearHistory();
    }

    private void StartupCheckBox_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _runtime.SetStartWithWindows(StartupCheckBox.IsChecked == true);
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
}

public sealed record HistoryItemViewModel(string Id, string Preview, string FormatSummary)
{
    public static HistoryItemViewModel FromSnapshot(ClipboardSnapshot snapshot)
    {
        return new HistoryItemViewModel(snapshot.Id, snapshot.Preview, string.Join(", ", snapshot.Formats));
    }
}
