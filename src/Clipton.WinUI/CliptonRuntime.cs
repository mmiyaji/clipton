using System.Text.Json;
using System.Reflection;
using Clipton.Core;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Win32;
using Drawing = System.Drawing;
using Drawing2D = System.Drawing.Drawing2D;
using Imaging = System.Drawing.Imaging;
using Forms = System.Windows.Forms;

namespace Clipton.WinUI;

public sealed class CliptonRuntime : IDisposable
{
    private const int HistorySaveDebounceMilliseconds = 500;
    private const int QuickMenuHotkeyDebounceMilliseconds = 160;
    private const int TempPasteMaxFiles = 100;
    private static readonly TimeSpan TempPasteMaxAge = TimeSpan.FromHours(24);
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly LocalizationCatalog _localization = new();
    private readonly JsonSettingsStore _settingsStore;
    private readonly EncryptedHistoryStore _historyStore;
    private readonly string _snippetPath;
    private readonly string _thumbnailPath;
    private readonly string _tempPastePath;
    private readonly object _historySaveGate = new();
    private CancellationTokenSource? _historySaveDebounce;
    private long _historySaveVersion;
    private HotkeyMessageWindow? _messageWindow;
    private Forms.NotifyIcon? _notifyIcon;
    private MainWindow? _mainWindow;
    private QuickMenuWindow? _quickMenuWindow;
    private IntPtr _pasteTargetWindow;
    private long _lastQuickMenuRequestTick;
    private int _quickMenuRequestPending;

    public CliptonRuntime()
    {
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Clipton");
        _settingsStore = new JsonSettingsStore(Path.Combine(appData, "settings.json"));
        _historyStore = new EncryptedHistoryStore(Path.Combine(appData, "history.dat"));
        _snippetPath = Path.Combine(appData, "snippets.json");
        _thumbnailPath = Path.Combine(appData, "thumbs");
        _tempPastePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Clipton", "TempPaste");
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

    public string EffectiveLocale => ResolveLocale(Settings.Locale);

    public string EffectiveTheme => ResolveTheme(Settings.Theme);

    public string Translate(string key) => _localization.Translate(EffectiveLocale, key);

    public string AppVersion => GetAppVersion();

    public string PackageStatus => GetPackageStatus();

    public bool IsExiting { get; private set; }

    public void Start()
    {
        EnsureDefaultSnippets();
        CleanupTempPasteFiles();
        _messageWindow = new HotkeyMessageWindow(ShowQuickMenuOnUiThread, CaptureClipboardOnUiThread);
        RegisterHotkey();
        CreateTrayIcon();
        CaptureClipboard();
    }

    public void ShowMainWindow()
    {
        _mainWindow ??= new MainWindow(this);
        _mainWindow.RefreshTexts();
        _mainWindow.RefreshItems();
        _mainWindow.ShowSettingsWindow();
    }

    public void OnMainWindowClosed(MainWindow window)
    {
        if (ReferenceEquals(_mainWindow, window))
        {
            _mainWindow = null;
        }
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

    public void PasteText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        ClipboardBridge.PutText(text);
        SendPaste();
    }

    public void PasteImage(string id, ImagePasteMode mode, bool sendPaste)
    {
        var item = History.Find(id);
        if (item?.ImagePng is not { Length: > 0 } imagePng)
        {
            return;
        }

        switch (mode)
        {
            case ImagePasteMode.Original:
                ClipboardBridge.Put(item, asPlainText: false);
                break;
            case ImagePasteMode.Png:
                ClipboardBridge.PutImagePng(imagePng);
                break;
            case ImagePasteMode.Jpeg:
                ClipboardBridge.PutImageJpeg(imagePng);
                break;
            case ImagePasteMode.File:
                ClipboardBridge.PutFileDrop(CreateTempImageFile(item));
                break;
            default:
                return;
        }

        if (sendPaste)
        {
            SendPaste();
        }
    }

    public async Task SetStartWithWindowsAsync(bool enabled)
    {
        var previous = Settings.StartWithWindows;
        Settings.StartWithWindows = enabled;
        var result = await StartupRegistration.SetEnabledAsync(enabled);
        if (enabled && result is StartupRegistrationResult.Disabled or StartupRegistrationResult.DisabledByPolicy or StartupRegistrationResult.DisabledByUser or StartupRegistrationResult.Unsupported)
        {
            Settings.StartWithWindows = false;
        }
        else if (!enabled && result is StartupRegistrationResult.Unsupported)
        {
            Settings.StartWithWindows = previous;
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

    public void SetMaxHistoryItems(int count)
    {
        Settings.MaxHistoryItems = Math.Clamp(count, 1, 1000);
        History.SetCapacity(Settings.MaxHistoryItems);
        SaveSettings();
        SaveHistory();
        _mainWindow?.RefreshItems();
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
            QueueHistorySave();
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

    public bool SetHotkey(string hotkey)
    {
        if (!HotkeyGesture.TryParse(hotkey, out var gesture))
        {
            return false;
        }

        var previousHotkey = Settings.Hotkey;
        Settings.Hotkey = gesture.ToString();
        if (!RegisterHotkey())
        {
            Settings.Hotkey = previousHotkey;
            RegisterHotkey();
            return false;
        }

        SaveSettings();
        return true;
    }

    public bool ResetHotkey()
    {
        return SetHotkey(HotkeyGesture.Default.ToString());
    }

    public void SuspendHotkey()
    {
        _messageWindow?.Unregister();
    }

    public void RestoreHotkey()
    {
        RegisterHotkey();
    }

    public void SetLocale(string locale)
    {
        Settings.Locale = NormalizeLocale(locale);
        SaveSettings();
        RefreshTrayText();
    }

    public void SetTheme(string theme)
    {
        Settings.Theme = NormalizeTheme(theme);
        SaveSettings();
        RefreshTrayIcon();
    }

    public void Dispose()
    {
        IsExiting = true;
        SaveHistory();
        _messageWindow?.Dispose();
        _notifyIcon?.Dispose();
        _quickMenuWindow?.Dismiss();
    }

    public void ExitApplication()
    {
        IsExiting = true;
        Application.Current.Exit();
    }

    private void CaptureClipboardOnUiThread()
    {
        _dispatcherQueue.TryEnqueue(CaptureClipboard);
    }

    private void ShowQuickMenuOnUiThread(IntPtr pasteTargetWindow)
    {
        var now = Environment.TickCount64;
        if (now - Volatile.Read(ref _lastQuickMenuRequestTick) < QuickMenuHotkeyDebounceMilliseconds)
        {
            return;
        }

        if (Interlocked.Exchange(ref _quickMenuRequestPending, 1) == 1)
        {
            return;
        }

        _pasteTargetWindow = pasteTargetWindow;
        if (!_dispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                Volatile.Write(ref _lastQuickMenuRequestTick, Environment.TickCount64);
                if (_quickMenuWindow is not null)
                {
                    _quickMenuWindow.FocusMenu();
                    return;
                }

                ShowQuickMenu();
            }
            finally
            {
                Volatile.Write(ref _quickMenuRequestPending, 0);
            }
        }))
        {
            Volatile.Write(ref _quickMenuRequestPending, 0);
        }
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
            QueueHistorySave();
            _mainWindow?.RefreshItems();
        }
    }

    private bool RegisterHotkey()
    {
        if (_messageWindow is null)
        {
            return false;
        }

        if (!HotkeyGesture.TryParse(Settings.Hotkey, out var gesture))
        {
            gesture = HotkeyGesture.Default;
        }

        return _messageWindow.Register(gesture);
    }

    private void CreateTrayIcon()
    {
        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = AppAssets.LoadTrayIcon(EffectiveTheme),
            Text = "Clipton",
            Visible = true
        };
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
            {
                _dispatcherQueue.TryEnqueue(ShowMainWindow);
            }
        };
        _notifyIcon.DoubleClick += (_, _) => _dispatcherQueue.TryEnqueue(ShowMainWindow);
        RefreshTrayText();
    }

    private void RefreshTrayIcon()
    {
        if (_notifyIcon is null)
        {
            return;
        }

        var oldIcon = _notifyIcon.Icon;
        _notifyIcon.Icon = AppAssets.LoadTrayIcon(EffectiveTheme);
        oldIcon?.Dispose();
    }

    private void RefreshTrayText()
    {
        if (_notifyIcon is null)
        {
            return;
        }

        var oldMenu = _notifyIcon.ContextMenuStrip;
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(CreateTrayMenuItem(Translate("History"), "\uE81C", (_, _) => _dispatcherQueue.TryEnqueue(ShowQuickMenu)));
        menu.Items.Add(CreateTrayMenuItem(Translate("Settings"), "\uE713", (_, _) => _dispatcherQueue.TryEnqueue(ShowMainWindow)));
        menu.Items.Add(CreateTrayMenuItem(Translate("Exit"), "\uE8BB", (_, _) => _dispatcherQueue.TryEnqueue(ExitApplication)));
        _notifyIcon.ContextMenuStrip = menu;
        oldMenu?.Dispose();
    }

    private static Forms.ToolStripMenuItem CreateTrayMenuItem(string text, string glyph, EventHandler onClick)
    {
        return new Forms.ToolStripMenuItem(text, CreateTrayMenuGlyph(glyph), onClick);
    }

    private static Drawing.Bitmap CreateTrayMenuGlyph(string glyph)
    {
        const int size = 18;
        var bitmap = new Drawing.Bitmap(size, size);
        using var graphics = Drawing.Graphics.FromImage(bitmap);
        graphics.Clear(Drawing.Color.Transparent);
        graphics.TextRenderingHint = Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        using var font = new Drawing.Font("Segoe Fluent Icons", 10.5f, Drawing.FontStyle.Regular, Drawing.GraphicsUnit.Point);
        using var brush = new Drawing.SolidBrush(Drawing.Color.FromArgb(220, 72, 72, 72));
        using var format = new Drawing.StringFormat
        {
            Alignment = Drawing.StringAlignment.Center,
            LineAlignment = Drawing.StringAlignment.Center,
            FormatFlags = Drawing.StringFormatFlags.NoWrap
        };
        graphics.DrawString(glyph, font, brush, new Drawing.RectangleF(0, 0, size, size), format);
        return bitmap;
    }

    private void ShowQuickMenu()
    {
        if (_pasteTargetWindow == IntPtr.Zero)
        {
            _pasteTargetWindow = NativeMethods.GetForegroundWindow();
        }

        CaptureClipboard();

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
                    $"{rangeCount} items",
                    ">",
                    "Enter",
                    () => { },
                    LazyChildren: () => olderItems.Skip(rangeOffset).Take(rangeCount).Select(CreateHistoryMenuItem).ToArray()));
            }
        }

        var snippetItems = CreateSnippetMenuItems(Snippets.Snippets);
        if (menuItems.Count > 0 && snippetItems.Count > 0)
        {
            menuItems.Add(QuickMenuItem.Separator());
        }

        menuItems.AddRange(snippetItems);

        if (menuItems.Count > 0)
        {
            menuItems.Add(QuickMenuItem.Separator());
            menuItems.Add(new QuickMenuItem(
                Translate("Settings"),
                "Clipton",
                "*",
                "Enter",
                ShowMainWindow,
                IconGlyph: "\uE713",
                IconFontFamily: "Segoe Fluent Icons"));
        }

        if (menuItems.Count == 0)
        {
            menuItems.Add(new QuickMenuItem(
                Translate("HistoryEmpty"),
                Translate("Settings"),
                "-",
                "Enter",
                ShowMainWindow,
                IconGlyph: "\uE713",
                IconFontFamily: "Segoe Fluent Icons"));
        }

        _quickMenuWindow?.Dismiss();
        var quickMenuWindow = new QuickMenuWindow(
            Translate("History"),
            menuItems,
            EffectiveTheme,
            Settings.SimpleContextMenuMode,
            Translate("Search"),
            Translate("SearchPrompt"),
            Translate("Search"),
            Translate("Cancel"),
            Translate("NoSearchResults"));
        _quickMenuWindow = quickMenuWindow;
        quickMenuWindow.Dismissed += (_, _) =>
        {
            if (ReferenceEquals(_quickMenuWindow, quickMenuWindow))
            {
                _quickMenuWindow = null;
            }
        };
        quickMenuWindow.FocusMenu();
    }

    private void SaveSettings()
    {
        _settingsStore.Save(Settings);
    }

    private static string NormalizeLocale(string locale)
    {
        if (string.Equals(locale, "system", StringComparison.OrdinalIgnoreCase))
        {
            return "system";
        }

        return string.Equals(locale, "ja", StringComparison.OrdinalIgnoreCase) ? "ja" : "en";
    }

    private static string NormalizeTheme(string theme)
    {
        if (string.Equals(theme, "system", StringComparison.OrdinalIgnoreCase))
        {
            return "system";
        }

        return string.Equals(theme, "dark", StringComparison.OrdinalIgnoreCase) ? "dark" : "light";
    }

    private static string ResolveLocale(string locale)
    {
        if (!string.Equals(locale, "system", StringComparison.OrdinalIgnoreCase))
        {
            return NormalizeLocale(locale);
        }

        return string.Equals(System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, "ja", StringComparison.OrdinalIgnoreCase)
            ? "ja"
            : "en";
    }

    private static string ResolveTheme(string theme)
    {
        if (!string.Equals(theme, "system", StringComparison.OrdinalIgnoreCase))
        {
            return NormalizeTheme(theme);
        }

        return IsWindowsAppThemeDark() ? "dark" : "light";
    }

    private static bool IsWindowsAppThemeDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch
        {
            return false;
        }
    }

    private static string GetAppVersion()
    {
        var assembly = typeof(CliptonRuntime).Assembly;
        var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            return informationalVersion;
        }

        return assembly.GetName().Version?.ToString() ?? "0.0.0";
    }

    private static string GetPackageStatus()
    {
        try
        {
            var id = Windows.ApplicationModel.Package.Current.Id;
            return $"{id.Name} {id.Version.Major}.{id.Version.Minor}.{id.Version.Build}.{id.Version.Revision}";
        }
        catch (InvalidOperationException)
        {
            return "Unpackaged";
        }
    }

    private void SaveHistory()
    {
        Interlocked.Increment(ref _historySaveVersion);
        lock (_historySaveGate)
        {
            _historySaveDebounce?.Cancel();
            _historySaveDebounce = null;
        }

        if (Settings.PersistEncryptedHistory)
        {
            _historyStore.Save(History.Items.ToArray());
        }
        else
        {
            _historyStore.Delete();
        }
    }

    private void QueueHistorySave()
    {
        if (!Settings.PersistEncryptedHistory)
        {
            return;
        }

        var snapshots = History.Items.ToArray();
        var version = Interlocked.Increment(ref _historySaveVersion);
        var cts = new CancellationTokenSource();
        CancellationTokenSource? previous;
        lock (_historySaveGate)
        {
            previous = _historySaveDebounce;
            _historySaveDebounce = cts;
        }

        previous?.Cancel();

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(HistorySaveDebounceMilliseconds, cts.Token);
                if (version == Volatile.Read(ref _historySaveVersion))
                {
                    _historyStore.Save(snapshots);
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                lock (_historySaveGate)
                {
                    if (ReferenceEquals(_historySaveDebounce, cts))
                    {
                        _historySaveDebounce = null;
                    }
                }

                cts.Dispose();
            }
        });
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

    private void SendPaste()
    {
        RestorePasteTarget();
        NativeMethods.keybd_event(NativeMethods.VkControl, 0, 0, UIntPtr.Zero);
        NativeMethods.keybd_event(NativeMethods.VkV, 0, 0, UIntPtr.Zero);
        NativeMethods.keybd_event(NativeMethods.VkV, 0, NativeMethods.KeyeventfKeyup, UIntPtr.Zero);
        NativeMethods.keybd_event(NativeMethods.VkControl, 0, NativeMethods.KeyeventfKeyup, UIntPtr.Zero);
    }

    private void RestorePasteTarget()
    {
        if (_pasteTargetWindow != IntPtr.Zero)
        {
            NativeMethods.SetForegroundWindow(_pasteTargetWindow);
            Thread.Sleep(80);
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
        var header = item.Formats.Contains(ClipboardFormatKind.Image) ? Translate("Image") : display.Preview;
        var revealedHeader = IsMaskedHistoryItem(item) && !item.Formats.Contains(ClipboardFormatKind.Image)
            ? item.Preview
            : null;
        var plainText = ClipboardBridge.GetPlainText(item);
        var pasteOptions = item.ImagePng is { Length: > 0 }
            ? CreateImagePasteOptions(item)
            : CreateTextPasteOptions(plainText);
        return new QuickMenuItem(
            header,
            display.FormatSummary,
            GetKindLabel(item),
            !string.IsNullOrEmpty(plainText) ? "Enter / T" : "Enter",
            () => PasteHistoryItem(item.Id, asPlainText: false),
            !string.IsNullOrEmpty(plainText) ? () => PasteHistoryItem(item.Id, asPlainText: true) : null,
            PasteOptions: pasteOptions,
            IconGlyph: GetHistoryIconGlyph(item),
            IconFontFamily: GetHistoryIconFontFamily(item),
            IconImagePath: SaveHistoryThumbnail(item),
            RevealedTitle: revealedHeader,
            CapturedAt: item.CapturedAt);
    }

    private string GetHistoryIconGlyph(ClipboardSnapshot item)
    {
        return GetMaskedHistoryKind(item) switch
        {
            MaskedHistoryKind.RegisteredSnippet => "\uE8EC",
            MaskedHistoryKind.Sensitive => "\uE72E",
            _ => GetUnmaskedHistoryIconGlyph(item)
        };
    }

    private static string GetUnmaskedHistoryIconGlyph(ClipboardSnapshot item)
    {
        if (item.Formats.Contains(ClipboardFormatKind.FileDrop))
        {
            return "\uE8A5";
        }

        if (item.Formats.Contains(ClipboardFormatKind.Image))
        {
            return "\uEB9F";
        }

        if (item.Formats.Contains(ClipboardFormatKind.RichText) || item.Formats.Contains(ClipboardFormatKind.Html))
        {
            return "R";
        }

        return "Aa";
    }

    private string GetHistoryIconFontFamily(ClipboardSnapshot item)
    {
        return GetMaskedHistoryKind(item) != MaskedHistoryKind.None
            || item.Formats.Contains(ClipboardFormatKind.FileDrop)
            || item.Formats.Contains(ClipboardFormatKind.Image)
            ? "Segoe Fluent Icons"
            : "Segoe UI";
    }

    private string? SaveHistoryThumbnail(ClipboardSnapshot item)
    {
        if (item.ImagePng is not { Length: > 0 })
        {
            return null;
        }

        try
        {
            Directory.CreateDirectory(_thumbnailPath);
            var path = Path.Combine(_thumbnailPath, $"{item.Id}-96.png");
            if (File.Exists(path))
            {
                return path;
            }

            using var sourceStream = new MemoryStream(item.ImagePng);
            using var source = Drawing.Image.FromStream(sourceStream);
            const int size = 96;
            var scale = Math.Min((double)size / source.Width, (double)size / source.Height);
            var width = Math.Max(1, (int)Math.Round(source.Width * scale));
            var height = Math.Max(1, (int)Math.Round(source.Height * scale));
            using var thumbnail = new Drawing.Bitmap(size, size);
            using var graphics = Drawing.Graphics.FromImage(thumbnail);
            graphics.Clear(Drawing.Color.Transparent);
            graphics.InterpolationMode = Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = Drawing2D.PixelOffsetMode.HighQuality;
            graphics.SmoothingMode = Drawing2D.SmoothingMode.HighQuality;
            graphics.DrawImage(source, (size - width) / 2, (size - height) / 2, width, height);
            thumbnail.Save(path, Imaging.ImageFormat.Png);
            return path;
        }
        catch
        {
            return null;
        }
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

        if (Settings.MaskSensitiveContent && SensitiveContentDetector.CreateMaskedPreview(plainText) is { } maskedPreview)
        {
            return new HistoryItemViewModel(snapshot.Id, NormalizePreviewText(maskedPreview), $"{Translate("MaskedSensitive")} - {formats}");
        }

        return new HistoryItemViewModel(snapshot.Id, CreatePreviewText(snapshot, plainText), formats);
    }

    private bool IsMaskedHistoryItem(ClipboardSnapshot snapshot)
    {
        var plainText = ClipboardBridge.GetPlainText(snapshot);
        return Snippets.FindByText(plainText) is not null
            || (Settings.MaskSensitiveContent && SensitiveContentDetector.CreateMaskedPreview(plainText) is not null);
    }

    private MaskedHistoryKind GetMaskedHistoryKind(ClipboardSnapshot snapshot)
    {
        var plainText = ClipboardBridge.GetPlainText(snapshot);
        if (Snippets.FindByText(plainText) is not null)
        {
            return MaskedHistoryKind.RegisteredSnippet;
        }

        return Settings.MaskSensitiveContent && SensitiveContentDetector.CreateMaskedPreview(plainText) is not null
            ? MaskedHistoryKind.Sensitive
            : MaskedHistoryKind.None;
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
            () => PasteSnippet(snippet.Folder, snippet.Name),
            PasteOptions: CreateTextPasteOptions(snippet.Text),
            IconGlyph: "S");
    }

    private IReadOnlyList<QuickMenuPasteOption> CreateTextPasteOptions(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        return
        [
            new QuickMenuPasteOption(Translate("PastePlain"), "\uE8D2", () => PasteText(text)),
            new QuickMenuPasteOption(Translate("PasteNoLineBreaks"), "\uE8EE", () => PasteText(RemoveLineBreaks(text))),
            new QuickMenuPasteOption(Translate("PasteUppercase"), "AA", () => PasteText(text.ToUpperInvariant()), "Segoe UI"),
            new QuickMenuPasteOption(Translate("PasteLowercase"), "aa", () => PasteText(text.ToLowerInvariant()), "Segoe UI"),
            new QuickMenuPasteOption(Translate("PasteTrimmed"), "\uE8C6", () => PasteText(text.Trim()))
        ];
    }

    private static string RemoveLineBreaks(string text)
    {
        return string.Join(
            " ",
            text.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private IReadOnlyList<QuickMenuPasteOption> CreateImagePasteOptions(ClipboardSnapshot item)
    {
        if (item.ImagePng is not { Length: > 0 })
        {
            return [];
        }

        return
        [
            new QuickMenuPasteOption(Translate("PasteImageOriginal"), "\uEB9F", () => PasteImage(item.Id, ImagePasteMode.Original, sendPaste: true)),
            new QuickMenuPasteOption(Translate("PasteImagePng"), "PNG", () => PasteImage(item.Id, ImagePasteMode.Png, sendPaste: true), "Segoe UI"),
            new QuickMenuPasteOption(Translate("PasteImageJpeg"), "JPG", () => PasteImage(item.Id, ImagePasteMode.Jpeg, sendPaste: true), "Segoe UI"),
            new QuickMenuPasteOption(Translate("PasteImageFile"), "\uE8A5", () => PasteImage(item.Id, ImagePasteMode.File, sendPaste: true)),
            new QuickMenuPasteOption(Translate("CopyImageOnly"), "\uE8C8", () => PasteImage(item.Id, ImagePasteMode.Png, sendPaste: false))
        ];
    }

    private string CreateTempImageFile(ClipboardSnapshot item)
    {
        CleanupTempPasteFiles();
        Directory.CreateDirectory(_tempPastePath);
        var id = string.Concat(item.Id.Where(char.IsLetterOrDigit));
        if (string.IsNullOrEmpty(id))
        {
            id = "image";
        }

        var path = Path.Combine(_tempPastePath, $"paste-{id}-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}.png");
        File.WriteAllBytes(path, item.ImagePng!);
        return path;
    }

    private void CleanupTempPasteFiles()
    {
        try
        {
            var directory = new DirectoryInfo(_tempPastePath);
            if (!directory.Exists)
            {
                return;
            }

            var files = directory.GetFiles("paste-*.png")
                .OrderByDescending(file => file.CreationTimeUtc)
                .ToArray();
            var cutoff = DateTime.UtcNow - TempPasteMaxAge;
            for (var i = 0; i < files.Length; i++)
            {
                if (files[i].CreationTimeUtc < cutoff || i >= TempPasteMaxFiles)
                {
                    TryDeleteFile(files[i]);
                }
            }
        }
        catch
        {
            // Temp files are best-effort cleanup; paste behavior must not depend on cleanup success.
        }
    }

    private static void TryDeleteFile(FileInfo file)
    {
        try
        {
            file.Delete();
        }
        catch
        {
        }
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
}

internal enum MaskedHistoryKind
{
    None,
    Sensitive,
    RegisteredSnippet
}

public enum ImagePasteMode
{
    Original,
    Png,
    Jpeg,
    File
}
