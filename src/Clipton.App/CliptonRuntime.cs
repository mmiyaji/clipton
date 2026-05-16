using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using Clipton.Core;
using Application = System.Windows.Application;
using Brushes = System.Windows.Media.Brushes;
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
    private QuickMenuWindow? _quickMenuWindow;

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

    public void PasteSnippet(string folder, string name)
    {
        var snippet = Snippets.Find(folder, name);
        if (snippet is null)
        {
            return;
        }

        ClipboardBridge.Put(ClipboardBridge.FromSnippet(snippet), asPlainText: true);
        SendPaste();
    }

    public async Task SetStartWithWindowsAsync(bool enabled)
    {
        Settings.StartWithWindows = enabled;
        var result = await StartupRegistration.SetEnabledAsync(enabled);
        if (enabled && result is StartupRegistrationResult.Disabled or StartupRegistrationResult.DisabledByPolicy or StartupRegistrationResult.DisabledByUser or StartupRegistrationResult.Unsupported)
        {
            Settings.StartWithWindows = false;
        }

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
        Settings.HistoryPersistenceConfigured = true;
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

    public void SetMaskSensitiveContent(bool enabled)
    {
        Settings.MaskSensitiveContent = enabled;
        SaveSettings();
    }

    public void SetFolderMode(bool enabled)
    {
        Settings.FolderMode = enabled;
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

    public void UpsertSnippet(string folder, string name, string text)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        Snippets.Upsert(new Snippet(name.Trim(), text, folder));
        SaveSnippets(_snippetPath, Snippets);
        _mainWindow?.RefreshItems();
    }

    public void RemoveSnippet(string folder, string name)
    {
        if (Snippets.Remove(folder, name))
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

    public void SetTheme(string theme)
    {
        Settings.Theme = string.Equals(theme, "dark", StringComparison.OrdinalIgnoreCase) ? "dark" : "light";
        SaveSettings();
    }

    public void Dispose()
    {
        SaveHistory();
        _messageWindow?.Dispose();
        _notifyIcon?.Dispose();
        _quickMenuWindow?.Close();
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
        _quickMenuWindow?.Close();

        var menuItems = new List<QuickMenuItem>();

        var historyItems = History.Items.ToArray();
        var directHistoryItems = Settings.FolderMode ? historyItems.Take(5) : historyItems.Take(20);
        foreach (var item in directHistoryItems)
        {
            menuItems.Add(CreateHistoryMenuItem(item));
        }

        if (Settings.FolderMode && historyItems.Length > 5)
        {
            var olderItems = historyItems.Skip(5).ToArray();
            for (var start = 0; start < olderItems.Length; start += 50)
            {
                var rangeItems = olderItems.Skip(start).Take(50).Select(CreateHistoryMenuItem).ToArray();
                var rangeStart = start + 1;
                var rangeEnd = start + rangeItems.Length;
                menuItems.Add(new QuickMenuItem(
                    $"{rangeStart}~{rangeEnd}",
                    $"{rangeItems.Length} items",
                    ">",
                    "Enter",
                    Brushes.DimGray,
                    () => { },
                    Children: rangeItems));
            }
        }

        menuItems.AddRange(CreateSnippetMenuItems(Snippets.Snippets));

        if (menuItems.Count > 0)
        {
            menuItems.Add(new QuickMenuItem(
                Translate("Settings"),
                "Clipton",
                "*",
                "Enter",
                Brushes.DimGray,
                ShowMainWindow));
        }

        if (menuItems.Count == 0)
        {
            menuItems.Add(new QuickMenuItem(
                Translate("HistoryEmpty"),
                Translate("Settings"),
                "-",
                "Enter",
                Brushes.Gray,
                ShowMainWindow,
                PlainTextInvoke: null,
                IsEnabled: true));
        }

        var quickMenuWindow = new QuickMenuWindow(Translate("History"), menuItems, Settings.Theme);
        _quickMenuWindow = quickMenuWindow;
        var cursor = Forms.Cursor.Position;
        quickMenuWindow.Left = cursor.X;
        quickMenuWindow.Top = cursor.Y;
        quickMenuWindow.Closed += (_, _) =>
        {
            if (ReferenceEquals(_quickMenuWindow, quickMenuWindow))
            {
                _quickMenuWindow = null;
            }
        };

        quickMenuWindow.Show();
        quickMenuWindow.FocusMenu();
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

    private static string GetKindLabel(ClipboardSnapshot item)
    {
        if (item.Formats.Contains(ClipboardFormatKind.FileDrop))
        {
            return "F";
        }

        if (item.Formats.Contains(ClipboardFormatKind.Image))
        {
            return "I";
        }

        if (item.Formats.Contains(ClipboardFormatKind.RichText) || item.Formats.Contains(ClipboardFormatKind.Html))
        {
            return "R";
        }

        return "T";
    }

    private QuickMenuItem CreateHistoryMenuItem(ClipboardSnapshot item)
    {
        var display = CreateHistoryItemViewModel(item);
        var header = item.Formats.Contains(ClipboardFormatKind.Image) ? Translate("Image") : display.Preview;
        return new QuickMenuItem(
            header,
            display.FormatSummary,
            GetKindLabel(item),
            !string.IsNullOrEmpty(item.Text) ? "Enter / T" : "Enter",
            Brushes.SteelBlue,
            () => PasteHistoryItem(item.Id, asPlainText: false),
            !string.IsNullOrEmpty(item.Text) ? () => PasteHistoryItem(item.Id, asPlainText: true) : null,
            PreviewImage: CreatePreviewImage(item));
    }

    public HistoryItemViewModel CreateHistoryItemViewModel(ClipboardSnapshot snapshot)
    {
        var formats = string.Join(", ", snapshot.Formats);
        var snippet = Snippets.FindByText(snapshot.Text);
        if (snippet is not null)
        {
            return new HistoryItemViewModel(snapshot.Id, snippet.DisplayName, $"{Translate("RegisteredSnippetMasked")} - {formats}");
        }

        if (Settings.MaskSensitiveContent && SensitiveContentDetector.ShouldMask(snapshot.Text))
        {
            return new HistoryItemViewModel(snapshot.Id, Translate("MaskedSensitive"), formats);
        }

        return new HistoryItemViewModel(snapshot.Id, snapshot.Preview, formats);
    }

    private IReadOnlyList<QuickMenuItem> CreateSnippetMenuItems(IEnumerable<Snippet> snippets)
    {
        return CreateSnippetMenuItems(snippets, string.Empty);
    }

    private IReadOnlyList<QuickMenuItem> CreateSnippetMenuItems(IEnumerable<Snippet> snippets, string parentFolder)
    {
        var normalizedParent = NormalizeFolder(parentFolder);
        var directSnippets = snippets
            .Where(snippet => NormalizeFolder(snippet.Folder) == normalizedParent)
            .OrderBy(snippet => snippet.Name, StringComparer.OrdinalIgnoreCase)
            .Select(CreateSnippetMenuItem)
            .ToList();

        var childFolders = snippets
            .Select(snippet => snippet.Folder)
            .Select(folder => GetImmediateChildFolder(folder, normalizedParent))
            .Where(folder => !string.IsNullOrEmpty(folder))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(folder => folder, StringComparer.OrdinalIgnoreCase);

        foreach (var childFolder in childFolders)
        {
            var fullFolder = string.IsNullOrEmpty(normalizedParent) ? childFolder : $"{normalizedParent}/{childFolder}";
            var children = CreateSnippetMenuItems(snippets, fullFolder);
            directSnippets.Add(new QuickMenuItem(
                childFolder,
                Translate("Snippets"),
                ">",
                "Enter",
                Brushes.DarkOrange,
                () => { },
                Children: children));
        }

        return directSnippets;
    }

    private QuickMenuItem CreateSnippetMenuItem(Snippet snippet)
    {
        return new QuickMenuItem(
            snippet.Name,
            string.IsNullOrWhiteSpace(snippet.Folder) ? Translate("Snippets") : snippet.Folder,
            "S",
            "Enter",
            Brushes.DarkOrange,
            () => PasteSnippet(snippet.Folder, snippet.Name));
    }

    private static string NormalizeFolder(string? folder)
    {
        return string.Join(
            "/",
            (folder ?? string.Empty)
                .Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static string GetImmediateChildFolder(string? folder, string parentFolder)
    {
        var normalized = NormalizeFolder(folder);
        var parent = NormalizeFolder(parentFolder);
        if (string.IsNullOrEmpty(normalized) || normalized == parent)
        {
            return string.Empty;
        }

        if (!string.IsNullOrEmpty(parent))
        {
            var prefix = $"{parent}/";
            if (!normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            normalized = normalized[prefix.Length..];
        }

        var separator = normalized.IndexOf('/');
        return separator < 0 ? normalized : normalized[..separator];
    }

    private static BitmapImage? CreatePreviewImage(ClipboardSnapshot item)
    {
        if (item.ImagePng is not { Length: > 0 })
        {
            return null;
        }

        using var stream = new MemoryStream(item.ImagePng);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.DecodePixelWidth = 88;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
