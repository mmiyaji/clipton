using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Clipton.Core;
using Application = System.Windows.Application;
using Forms = System.Windows.Forms;

namespace Clipton.App;

public sealed class CliptonRuntime : IDisposable
{
    private readonly LocalizationCatalog _localization = new();
    private readonly JsonSettingsStore _settingsStore;
    private readonly EncryptedHistoryStore _historyStore;
    private readonly string _snippetPath;
    private HotkeyMessageWindow? _messageWindow;
    private Forms.NotifyIcon? _notifyIcon;
    private MainWindow? _mainWindow;

    public CliptonRuntime()
    {
        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Clipton");
        _settingsStore = new JsonSettingsStore(Path.Combine(appData, "settings.json"));
        _historyStore = new EncryptedHistoryStore(Path.Combine(appData, "history.dat"));
        _snippetPath = Path.Combine(appData, "snippets.json");
        Settings = _settingsStore.Load();
        History = new ClipboardHistory(Settings.MaxHistoryItems);
        Snippets = LoadSnippets(_snippetPath);

        if (Settings.PersistEncryptedHistory)
        {
            foreach (var snapshot in _historyStore.Load().Reverse())
            {
                History.Add(snapshot);
            }
        }
    }

    public CliptonSettings Settings { get; }

    public ClipboardHistory History { get; }

    public SnippetCatalog Snippets { get; }

    public string Translate(string key) => _localization.Translate(Settings.Locale, key);

    public void Start()
    {
        EnsureDefaultSnippets();
        _messageWindow = new HotkeyMessageWindow(ShowQuickMenu, CaptureClipboard);
        RegisterHotkey();
        CreateTrayIcon();
        CaptureClipboard();
    }

    public void ShowMainWindow()
    {
        _mainWindow ??= new MainWindow(this);
        _mainWindow.RefreshTexts();
        _mainWindow.RefreshItems();
        _mainWindow.Show();
        _mainWindow.Activate();
    }

    public void PasteHistoryItem(string id, bool asPlainText)
    {
        var item = History.Find(id);
        if (item is null)
        {
            return;
        }

        ClipboardBridge.Put(item, asPlainText || Settings.PastePlainTextByDefault);
        SendPaste();
    }

    public void PasteSnippet(string name)
    {
        var snippet = Snippets.Snippets.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
        if (snippet is null)
        {
            return;
        }

        ClipboardBridge.Put(ClipboardBridge.FromSnippet(snippet), asPlainText: true);
        SendPaste();
    }

    public void SetStartWithWindows(bool enabled)
    {
        Settings.StartWithWindows = enabled;
        StartupRegistration.SetEnabled(enabled);
        SaveSettings();
    }

    public void SetPauseCapture(bool paused)
    {
        Settings.PauseCapture = paused;
        SaveSettings();
    }

    public void SetPersistEncryptedHistory(bool enabled)
    {
        Settings.PersistEncryptedHistory = enabled;
        if (enabled)
        {
            SaveHistory();
        }
        else
        {
            _historyStore.Delete();
        }

        SaveSettings();
    }

    public void RemoveHistoryItem(string id)
    {
        if (History.Remove(id))
        {
            SaveHistory();
            _mainWindow?.RefreshItems();
        }
    }

    public void UpsertSnippet(string name, string text)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        Snippets.Upsert(new Snippet(name.Trim(), text));
        SaveSnippets(_snippetPath, Snippets);
        _mainWindow?.RefreshItems();
    }

    public void RemoveSnippet(string name)
    {
        if (Snippets.Remove(name))
        {
            SaveSnippets(_snippetPath, Snippets);
            _mainWindow?.RefreshItems();
        }
    }

    public void ClearHistory()
    {
        History.Clear();
        SaveHistory();
        _mainWindow?.RefreshItems();
    }

    public void SetHotkey(string hotkey)
    {
        if (HotkeyGesture.TryParse(hotkey, out var gesture))
        {
            Settings.Hotkey = gesture.ToString();
            RegisterHotkey();
            SaveSettings();
        }
    }

    public void SetLocale(string locale)
    {
        Settings.Locale = locale;
        SaveSettings();
        RefreshTrayText();
    }

    public void Dispose()
    {
        _messageWindow?.Dispose();
        _notifyIcon?.Dispose();
    }

    private void CaptureClipboard()
    {
        if (Settings.PauseCapture)
        {
            return;
        }

        var snapshot = ClipboardBridge.Capture();
        if (snapshot is null)
        {
            return;
        }

        if (History.Add(snapshot))
        {
            SaveHistory();
            _mainWindow?.RefreshItems();
        }
    }

    private void RegisterHotkey()
    {
        if (_messageWindow is null)
        {
            return;
        }

        if (!HotkeyGesture.TryParse(Settings.Hotkey, out var gesture))
        {
            gesture = HotkeyGesture.Default;
        }

        _messageWindow.Register(gesture);
    }

    private void CreateTrayIcon()
    {
        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "Clipton",
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => Application.Current.Dispatcher.Invoke(ShowMainWindow);
        RefreshTrayText();
    }

    private void RefreshTrayText()
    {
        if (_notifyIcon is null)
        {
            return;
        }

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(Translate("History"), null, (_, _) => Application.Current.Dispatcher.Invoke(ShowQuickMenu));
        menu.Items.Add(Translate("Settings"), null, (_, _) => Application.Current.Dispatcher.Invoke(ShowMainWindow));
        menu.Items.Add(Translate("Exit"), null, (_, _) => Application.Current.Dispatcher.Invoke(Application.Current.Shutdown));
        _notifyIcon.ContextMenuStrip = menu;
    }

    private void ShowQuickMenu()
    {
        CaptureClipboard();
        var menu = new ContextMenu { Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint };

        foreach (var item in History.Items.Take(20))
        {
            var header = Trim(item.Preview, 80);
            var pasteOriginal = new MenuItem { Header = header };
            pasteOriginal.Click += (_, _) => PasteHistoryItem(item.Id, asPlainText: false);
            menu.Items.Add(pasteOriginal);

            if (!string.IsNullOrEmpty(item.Text))
            {
                var pastePlain = new MenuItem { Header = $"  {Translate("PastePlain")}: {header}" };
                pastePlain.Click += (_, _) => PasteHistoryItem(item.Id, asPlainText: true);
                menu.Items.Add(pastePlain);
            }
        }

        if (History.Items.Count > 0 && Snippets.Snippets.Count > 0)
        {
            menu.Items.Add(new Separator());
        }

        foreach (var snippet in Snippets.Snippets)
        {
            var menuItem = new MenuItem { Header = $"{Translate("Snippets")}: {snippet.Name}" };
            menuItem.Click += (_, _) => PasteSnippet(snippet.Name);
            menu.Items.Add(menuItem);
        }

        menu.Items.Add(new Separator());
        var settings = new MenuItem { Header = Translate("Settings") };
        settings.Click += (_, _) => ShowMainWindow();
        menu.Items.Add(settings);

        menu.IsOpen = true;
    }

    private void SaveSettings()
    {
        _settingsStore.Save(Settings);
    }

    private void SaveHistory()
    {
        if (Settings.PersistEncryptedHistory)
        {
            _historyStore.Save(History.Items);
        }
        else
        {
            _historyStore.Delete();
        }
    }

    private void EnsureDefaultSnippets()
    {
        if (Snippets.Snippets.Count > 0)
        {
            return;
        }

        Snippets.Upsert(new Snippet("Email", "hello@example.com"));
        Snippets.Upsert(new Snippet("Greeting", "Hello,"));
        SaveSnippets(_snippetPath, Snippets);
    }

    private static SnippetCatalog LoadSnippets(string path)
    {
        var catalog = new SnippetCatalog();
        if (!File.Exists(path))
        {
            return catalog;
        }

        var snippets = JsonSerializer.Deserialize<Snippet[]>(File.ReadAllText(path)) ?? [];
        foreach (var snippet in snippets)
        {
            catalog.Upsert(snippet);
        }

        return catalog;
    }

    private static void SaveSnippets(string path, SnippetCatalog catalog)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(catalog.Snippets, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void SendPaste()
    {
        NativeMethods.keybd_event(NativeMethods.VkControl, 0, 0, UIntPtr.Zero);
        NativeMethods.keybd_event(NativeMethods.VkV, 0, 0, UIntPtr.Zero);
        NativeMethods.keybd_event(NativeMethods.VkV, 0, NativeMethods.KeyeventfKeyup, UIntPtr.Zero);
        NativeMethods.keybd_event(NativeMethods.VkControl, 0, NativeMethods.KeyeventfKeyup, UIntPtr.Zero);
    }

    private static string Trim(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : string.Concat(value.AsSpan(0, maxLength - 1), "...");
    }
}
