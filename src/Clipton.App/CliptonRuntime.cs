using System.IO;
using System.Globalization;
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

/// <summary>
/// Coordinates the legacy WPF app lifetime, settings, clipboard capture and paste actions.
/// </summary>
/// <remarks>
/// This runtime mirrors the same core concepts as the WinUI runtime but keeps WPF-specific
/// tray, window and clipboard integrations isolated from <c>Clipton.Core</c>.
/// </remarks>
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

    /// <summary>
    /// Creates the runtime and loads settings, snippets and persisted history.
    /// </summary>
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

    /// <summary>Current normalized settings model.</summary>
    public CliptonSettings Settings { get; }

    /// <summary>Resident clipboard history.</summary>
    public ClipboardHistory History { get; }

    /// <summary>In-memory snippet catalog.</summary>
    public SnippetCatalog Snippets { get; }

    /// <summary>Translates a UI text key for the configured locale.</summary>
    public string Translate(string key) => _localization.Translate(Settings.Locale, key);

    private string FormatItemCount(int count)
    {
        return string.Format(CultureInfo.GetCultureInfo(LocalizationCatalog.ResolveLocale(Settings.Locale)), Translate("QuickMenuItemCount"), count);
    }

    /// <summary>Starts clipboard capture, hotkey registration and the tray icon.</summary>
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

    public void ShowNewSnippetEditor()
    {
        ShowMainWindow();
        _mainWindow?.OpenNewSnippetEditor();
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

        ClipboardBridge.PutText(SnippetTemplateRenderer.Render(snippet.Text));
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

    /// <summary>
    /// Enables or disables encrypted local history persistence.
    /// </summary>
    /// <remarks>
    /// Disabling persistence is also a user data deletion operation for this legacy app:
    /// resident history, pinned ids and encrypted files are cleared together.
    /// </remarks>
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
            History.Clear();
            Settings.PinnedHistoryIds = [];
            _historyStore.Delete();
            _mainWindow?.RefreshItems();
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

    public void SetSimpleContextMenuMode(bool enabled)
    {
        Settings.SimpleContextMenuMode = enabled;
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
        Settings.Locale = LocalizationCatalog.NormalizeLocale(locale);
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
            Icon = LoadTrayIcon(),
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
        menu.Items.Add(Translate("NewSnippet"), null, (_, _) => Application.Current.Dispatcher.Invoke(ShowNewSnippetEditor));
        menu.Items.Add(Translate("Settings"), null, (_, _) => Application.Current.Dispatcher.Invoke(ShowMainWindow));
        menu.Items.Add(Translate("Exit"), null, (_, _) => Application.Current.Dispatcher.Invoke(Application.Current.Shutdown));
        var previousMenu = _notifyIcon.ContextMenuStrip;
        _notifyIcon.ContextMenuStrip = menu;
        previousMenu?.Dispose();
    }

    private static System.Drawing.Icon LoadTrayIcon()
    {
        var streamInfo = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/Clipton.ico"));
        if (streamInfo is null)
        {
            return System.Drawing.SystemIcons.Application;
        }

        return new System.Drawing.Icon(streamInfo.Stream);
    }

    private void ShowQuickMenu()
    {
        CaptureClipboard();
        _quickMenuWindow?.Close();

        var menuItems = new List<QuickMenuItem>();

        var historyItems = History.Items.ToArray();
        var directHistoryItems = Settings.FolderMode ? historyItems.Take(3) : historyItems.Take(20);
        foreach (var item in directHistoryItems)
        {
            menuItems.Add(CreateHistoryMenuItem(item));
        }

        if (Settings.FolderMode && historyItems.Length > 3)
        {
            var olderItems = historyItems.Skip(3).ToArray();
            for (var start = 0; start < olderItems.Length; start += 50)
            {
                var rangeStart = start + 1;
                var rangeCount = Math.Min(50, olderItems.Length - start);
                var rangeEnd = start + rangeCount;
                var rangeOffset = start;
                menuItems.Add(new QuickMenuItem(
                    $"{rangeStart}~{rangeEnd}",
                    FormatItemCount(rangeCount),
                    ">",
                    "Enter",
                    Brushes.DimGray,
                    () => { },
                    LazyChildren: () => olderItems.Skip(rangeOffset).Take(rangeCount).Select(CreateHistoryMenuItem).ToArray()));
            }
        }

        menuItems.AddRange(CreateSnippetMenuItems(Snippets.Snippets));

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

        menuItems.Add(new QuickMenuItem(
            Translate("NewSnippet"),
            Translate("Snippets"),
            "+",
            "Enter",
            Brushes.DarkOrange,
            ShowNewSnippetEditor));
        menuItems.Add(new QuickMenuItem(
            Translate("Settings"),
            "Clipton",
            "*",
            "Enter",
            Brushes.DimGray,
            ShowMainWindow));

        var quickMenuWindow = new QuickMenuWindow(
            Translate("History"),
            menuItems,
            Settings.Theme,
            Settings.SimpleContextMenuMode,
            Translate("QuickMenuKeyboardHelp"));
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
        var changed = false;
        if (Snippets.Snippets.Count == 0)
        {
            Snippets.Upsert(new Snippet("Email", "hello@example.com"));
            Snippets.Upsert(new Snippet("Greeting", "Hello,"));
            changed = true;
        }

        changed |= AddDefaultSnippet("datetime", "today", "{{date}}");
        changed |= AddDefaultSnippet("datetime", "now", "{{time}}");
        if (changed)
        {
            SaveSnippets(_snippetPath, Snippets);
        }
    }

    private bool AddDefaultSnippet(string folder, string name, string text)
    {
        if (Snippets.Find(folder, name) is not null)
        {
            return false;
        }

        Snippets.Upsert(new Snippet(name, text, folder));
        return true;
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
        WaitForModifierKeyRelease();
        NativeMethods.keybd_event(NativeMethods.VkControl, 0, 0, UIntPtr.Zero);
        NativeMethods.keybd_event(NativeMethods.VkV, 0, 0, UIntPtr.Zero);
        Thread.Sleep(20);
        NativeMethods.keybd_event(NativeMethods.VkV, 0, NativeMethods.KeyeventfKeyup, UIntPtr.Zero);
        NativeMethods.keybd_event(NativeMethods.VkControl, 0, NativeMethods.KeyeventfKeyup, UIntPtr.Zero);
    }

    private static void WaitForModifierKeyRelease()
    {
        static bool IsDown(int key) => (NativeMethods.GetAsyncKeyState(key) & 0x8000) != 0;

        var deadline = Environment.TickCount64 + 600;
        while (Environment.TickCount64 < deadline)
        {
            if (!IsDown(NativeMethods.VkShift)
                && !IsDown(NativeMethods.VkMenu)
                && !IsDown(NativeMethods.VkLWin)
                && !IsDown(NativeMethods.VkRWin))
            {
                return;
            }

            Thread.Sleep(15);
        }
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
        var plainText = ClipboardBridge.GetPlainText(item);
        return new QuickMenuItem(
            display.Preview,
            display.FormatSummary,
            GetKindLabel(item),
            !string.IsNullOrEmpty(plainText) ? "Enter / T" : "Enter",
            Brushes.SteelBlue,
            () => PasteHistoryItem(item.Id, asPlainText: false),
            !string.IsNullOrEmpty(plainText) ? () => PasteHistoryItem(item.Id, asPlainText: true) : null,
            PreviewImage: CreatePreviewImage(item));
    }

    public HistoryItemViewModel CreateHistoryItemViewModel(ClipboardSnapshot snapshot)
    {
        var formats = string.Join(", ", snapshot.Formats);
        var plainText = ClipboardBridge.GetPlainText(snapshot);
        var snippet = Snippets.FindByText(plainText);
        if (snippet is not null)
        {
            return new HistoryItemViewModel(snapshot.Id, snippet.DisplayName, $"{Translate("RegisteredSnippetMasked")} - {formats}");
        }

        var previewScanText = SensitiveContentDetector.CreatePreviewScanText(plainText);
        if (Settings.MaskSensitiveContent && SensitiveContentDetector.CreateMaskedPreview(previewScanText) is { } maskedPreview)
        {
            return new HistoryItemViewModel(snapshot.Id, NormalizePreviewText(maskedPreview), $"{Translate("MaskedSensitive")} - {formats}");
        }

        return new HistoryItemViewModel(snapshot.Id, CreatePreviewText(snapshot, plainText), formats);
    }

    private static string CreatePreviewText(ClipboardSnapshot snapshot, string? plainText)
    {
        return string.IsNullOrWhiteSpace(plainText)
            ? snapshot.Preview
            : NormalizePreviewText(plainText);
    }

    private static string NormalizePreviewText(string text)
    {
        return text.ReplaceLineEndings(" ").Trim();
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
