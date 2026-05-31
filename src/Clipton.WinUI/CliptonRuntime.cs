using System.Text.Json;
using System.Reflection;
using System.Text.RegularExpressions;
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
    private const int TrayMenuItemHeight = 36;
    private const int TrayMenuItemMinWidth = 168;
    private const int TrayMenuIconSize = 16;
    private const int TrayMenuTextLeft = 44;
    private const int TempPasteMaxFiles = 100;
    private static readonly TimeSpan TempPasteMaxAge = TimeSpan.FromHours(24);
    private static readonly Regex UrlRegex = new(@"\b(?:https?|ftp)://[^\s<>()""']+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
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
    private bool _clipboardServicesStarted;
    private CancellationTokenSource? _clipboardCaptureDelay;

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
        CreateTrayIcon();
        if (Settings.InitialLaunchCompleted)
        {
            StartClipboardServices();
        }
    }

    public void ShowStartupWindowIfNeeded(bool forceShow)
    {
        if (forceShow)
        {
            ShowMainWindow();
            _mainWindow?.ShowOnboardingIfNeeded();
            return;
        }

        if (!Settings.InitialLaunchCompleted)
        {
            ShowMainWindow();
            _mainWindow?.ShowOnboardingIfNeeded();
            return;
        }

        if (!Settings.HideSettingsWindowOnStartup)
        {
            ShowMainWindow();
        }
    }

    public void ShowMainWindow()
    {
        _mainWindow ??= new MainWindow(this);
        _mainWindow.RefreshTexts();
        _mainWindow.RefreshItems();
        _mainWindow.ShowSettingsWindow();
    }

    public void CompleteOnboarding()
    {
        MarkInitialLaunchCompleted();
        StartClipboardServices();
        _mainWindow?.RefreshTexts();
        _mainWindow?.RefreshItems();
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

        ClipboardBridge.PutText(SnippetTemplateRenderer.Render(snippet.Text));
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

    public IReadOnlyList<QuickMenuPasteOption> CreateHistoryContextOptions(string id)
    {
        var item = History.Find(id);
        if (item is null)
        {
            return [];
        }

        if (item.Formats.Contains(ClipboardFormatKind.Image))
        {
            return CreateImagePasteOptions(item);
        }

        var plainText = ClipboardBridge.GetPlainText(item);
        var options = new List<QuickMenuPasteOption>
        {
            new(Translate("PasteOriginal"), "\uE77F", () => PasteHistoryItem(item.Id, asPlainText: false))
        };

        options.AddRange(CreateTextPasteOptions(plainText, item.Id));
        return options;
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

    public void SetHideSettingsWindowOnStartup(bool enabled)
    {
        Settings.HideSettingsWindowOnStartup = enabled;
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

    public void SetMaskDefinitionOptions(int visiblePrefixLength, string[] customPatterns)
    {
        Settings.MaskVisiblePrefixLength = Math.Clamp(visiblePrefixLength, 0, 12);
        Settings.CustomMaskPatterns = SensitiveContentDetector.ValidateCustomPatterns(customPatterns);
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

    public void SetClipboardCaptureDelay(int milliseconds)
    {
        Settings.ClipboardCaptureDelayMilliseconds = NormalizeClipboardCaptureDelay(milliseconds);
        SaveSettings();
    }

    public void SetFolderMode(bool enabled)
    {
        Settings.FolderMode = enabled;
        SaveSettings();
    }

    public void SetQuickMenuImagePreviewSize(string size)
    {
        Settings.QuickMenuImagePreviewSize = NormalizeQuickMenuImagePreviewSize(size);
        SaveSettings();
    }

    public void SetQuickMenuShowCapturedAt(bool enabled)
    {
        Settings.QuickMenuShowCapturedAt = enabled;
        SaveSettings();
    }

    public void SetQuickMenuShowShortcutHints(bool enabled)
    {
        Settings.QuickMenuShowShortcutHints = enabled;
        SaveSettings();
    }

    public void SetQuickMenuShortcut(string action, string shortcut)
    {
        Settings.QuickMenuShortcuts ??= new QuickMenuShortcutSettings();
        switch (action)
        {
            case nameof(QuickMenuShortcutSettings.Search):
                Settings.QuickMenuShortcuts.Search = NormalizeQuickMenuShortcut(
                    shortcut,
                    QuickMenuShortcutSettings.DefaultSearch,
                    ["Ctrl+S", "Ctrl+F", "S", "F"]);
                break;
            case nameof(QuickMenuShortcutSettings.PastePlainText):
                Settings.QuickMenuShortcuts.PastePlainText = NormalizeQuickMenuShortcut(
                    shortcut,
                    QuickMenuShortcutSettings.DefaultPastePlainText,
                    ["T", "P", "Ctrl+T", "Ctrl+P"]);
                break;
            case nameof(QuickMenuShortcutSettings.ToggleMaskReveal):
                Settings.QuickMenuShortcuts.ToggleMaskReveal = NormalizeQuickMenuShortcut(
                    shortcut,
                    QuickMenuShortcutSettings.DefaultToggleMaskReveal,
                    ["M", "Ctrl+M"]);
                break;
            case nameof(QuickMenuShortcutSettings.ToggleCapturedAt):
                Settings.QuickMenuShortcuts.ToggleCapturedAt = NormalizeQuickMenuShortcut(
                    shortcut,
                    QuickMenuShortcutSettings.DefaultToggleCapturedAt,
                    ["Ctrl+D", "D"]);
                break;
            default:
                return;
        }

        SaveSettings();
    }

    public void RemoveHistoryItem(string id)
    {
        if (History.Remove(id))
        {
            UnpinHistoryItem(id, refresh: false);
            DeleteHistoryImageFiles(id);
            QueueHistorySave();
            _mainWindow?.RefreshItems();
        }
    }

    public void TogglePinnedHistoryItem(string id)
    {
        if (IsHistoryPinned(id))
        {
            UnpinHistoryItem(id, refresh: true);
            return;
        }

        var ids = Settings.PinnedHistoryIds.ToList();
        ids.Add(id);
        Settings.PinnedHistoryIds = ids.Distinct(StringComparer.Ordinal).ToArray();
        SaveSettings();
        _mainWindow?.RefreshItems();
    }

    public bool IsHistoryPinned(string id) => Settings.PinnedHistoryIds.Contains(id, StringComparer.Ordinal);

    private void UnpinHistoryItem(string id, bool refresh)
    {
        var ids = Settings.PinnedHistoryIds.Where(item => !string.Equals(item, id, StringComparison.Ordinal)).ToArray();
        if (ids.Length == Settings.PinnedHistoryIds.Length)
        {
            return;
        }

        Settings.PinnedHistoryIds = ids;
        SaveSettings();
        if (refresh)
        {
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

    public int ExportHistory(string path)
    {
        var items = History.Items.Select(HistoryExportItemDto.FromSnapshot).ToArray();
        WriteExportFile(path, new HistoryExportDto(1, DateTimeOffset.UtcNow, items));
        return items.Length;
    }

    public int ImportHistory(string path)
    {
        var dto = ReadExportFile<HistoryExportDto>(path);
        var items = dto.Items ?? throw new InvalidOperationException("The selected file does not contain history items.");
        var before = History.Items.Count;
        foreach (var item in items.Reverse())
        {
            History.Add(item.ToSnapshot());
        }

        SaveHistory();
        _mainWindow?.RefreshItems();
        return Math.Max(0, History.Items.Count - before);
    }

    public int ExportSnippets(string path)
    {
        var items = Snippets.Snippets.ToArray();
        WriteExportFile(path, new SnippetExportDto(1, DateTimeOffset.UtcNow, items));
        return items.Length;
    }

    public int ImportSnippets(string path)
    {
        var dto = ReadExportFile<SnippetExportDto>(path);
        var items = dto.Items ?? throw new InvalidOperationException("The selected file does not contain snippet items.");
        var imported = 0;
        foreach (var snippet in items)
        {
            if (string.IsNullOrWhiteSpace(snippet.Name))
            {
                continue;
            }

            Snippets.Upsert(snippet);
            imported++;
        }

        SaveSnippets(_snippetPath, Snippets);
        _mainWindow?.RefreshItems();
        return imported;
    }

    public void ClearHistory()
    {
        History.Clear();
        Settings.PinnedHistoryIds = [];
        SaveSettings();
        SaveHistory();
        ClearHistoryImageFiles();
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
        RefreshTrayText();
    }

    public void Dispose()
    {
        IsExiting = true;
        _clipboardCaptureDelay?.Cancel();
        _clipboardCaptureDelay?.Dispose();
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

    private void StartClipboardServices()
    {
        if (_clipboardServicesStarted)
        {
            return;
        }

        _messageWindow = new HotkeyMessageWindow(ShowQuickMenuOnUiThread, CaptureClipboardOnUiThread);
        RegisterHotkey();
        CaptureClipboard();
        _clipboardServicesStarted = true;
    }

    public void ShowHistoryWindow()
    {
        ShowMainWindow();
        _mainWindow?.ShowHistoryPage();
    }

    private void CaptureClipboardOnUiThread()
    {
        var delay = Settings.ClipboardCaptureDelayMilliseconds;
        if (delay <= 0)
        {
            _dispatcherQueue.TryEnqueue(CaptureClipboard);
            return;
        }

        var captureDelay = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _clipboardCaptureDelay, captureDelay);
        previous?.Cancel();
        previous?.Dispose();
        var token = captureDelay.Token;
        _ = Task.Delay(delay, token).ContinueWith(task =>
        {
            if (!task.IsCanceled)
            {
                _dispatcherQueue.TryEnqueue(CaptureClipboard);
            }
        }, TaskScheduler.Default);
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
        var dark = string.Equals(EffectiveTheme, "dark", StringComparison.OrdinalIgnoreCase);
        var palette = TrayMenuPalette.Create(dark);
        var menu = new Forms.ContextMenuStrip
        {
            BackColor = palette.Background,
            ForeColor = palette.Text,
            Font = new Drawing.Font("Segoe UI Variable Text", 9f, Drawing.FontStyle.Regular, Drawing.GraphicsUnit.Point),
            ImageScalingSize = new Drawing.Size(TrayMenuIconSize, TrayMenuIconSize),
            Padding = new Forms.Padding(4),
            Renderer = new WinUiTrayMenuRenderer(palette),
            ShowCheckMargin = false,
            ShowImageMargin = true
        };
        menu.Items.Add(CreateTrayMenuItem(Translate("History"), "\uE81C", palette, (_, _) => _dispatcherQueue.TryEnqueue(ShowQuickMenu)));
        menu.Items.Add(CreateTrayMenuItem(Translate("Settings"), "\uE713", palette, (_, _) => _dispatcherQueue.TryEnqueue(ShowMainWindow)));
        menu.Items.Add(new Forms.ToolStripSeparator { Margin = new Forms.Padding(4, 3, 4, 3) });
        menu.Items.Add(CreateTrayMenuItem(Translate("Exit"), "\uE8BB", palette, (_, _) => _dispatcherQueue.TryEnqueue(ExitApplication)));
        _notifyIcon.ContextMenuStrip = menu;
        oldMenu?.Dispose();
    }

    private static Forms.ToolStripMenuItem CreateTrayMenuItem(string text, string glyph, TrayMenuPalette palette, EventHandler onClick)
    {
        return new Forms.ToolStripMenuItem(text, CreateTrayMenuGlyph(glyph, palette.Icon), onClick)
        {
            AutoSize = false,
            DisplayStyle = Forms.ToolStripItemDisplayStyle.ImageAndText,
            ForeColor = palette.Text,
            ImageAlign = Drawing.ContentAlignment.MiddleCenter,
            Margin = Forms.Padding.Empty,
            Padding = Forms.Padding.Empty,
            Size = new Drawing.Size(GetTrayMenuItemWidth(text), TrayMenuItemHeight),
            TextAlign = Drawing.ContentAlignment.MiddleLeft,
            TextImageRelation = Forms.TextImageRelation.ImageBeforeText
        };
    }

    private static int GetTrayMenuItemWidth(string text)
    {
        using var font = new Drawing.Font("Segoe UI Variable Text", 9f, Drawing.FontStyle.Regular, Drawing.GraphicsUnit.Point);
        var textSize = Forms.TextRenderer.MeasureText(text, font);
        return Math.Max(TrayMenuItemMinWidth, TrayMenuTextLeft + textSize.Width + 24);
    }

    private static Drawing.Bitmap CreateTrayMenuGlyph(string glyph, Drawing.Color color)
    {
        const int size = 20;
        var bitmap = new Drawing.Bitmap(size, size);
        using var graphics = Drawing.Graphics.FromImage(bitmap);
        graphics.Clear(Drawing.Color.Transparent);
        graphics.TextRenderingHint = Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        using var font = new Drawing.Font("Segoe Fluent Icons", 9.5f, Drawing.FontStyle.Regular, Drawing.GraphicsUnit.Point);
        using var brush = new Drawing.SolidBrush(color);
        using var format = new Drawing.StringFormat
        {
            Alignment = Drawing.StringAlignment.Center,
            LineAlignment = Drawing.StringAlignment.Center,
            FormatFlags = Drawing.StringFormatFlags.NoWrap
        };
        graphics.DrawString(glyph, font, brush, new Drawing.RectangleF(0, 0, size, size), format);
        return bitmap;
    }

    private sealed class WinUiTrayMenuRenderer(TrayMenuPalette palette) : Forms.ToolStripProfessionalRenderer
    {
        protected override void OnRenderToolStripBackground(Forms.ToolStripRenderEventArgs e)
        {
            using var brush = new Drawing.SolidBrush(palette.Background);
            e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(Forms.ToolStripRenderEventArgs e)
        {
            using var pen = new Drawing.Pen(palette.Border);
            var bounds = new Drawing.Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            e.Graphics.DrawRectangle(pen, bounds);
        }

        protected override void OnRenderImageMargin(Forms.ToolStripRenderEventArgs e)
        {
            using var brush = new Drawing.SolidBrush(palette.Background);
            e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        protected override void OnRenderMenuItemBackground(Forms.ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected && !e.Item.Pressed)
            {
                return;
            }

            var bounds = new Drawing.Rectangle(4, 2, e.Item.Width - 8, e.Item.Height - 4);
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return;
            }

            var previousMode = e.Graphics.SmoothingMode;
            e.Graphics.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias;
            using var path = CreateRoundedRectangle(bounds, 4);
            using var brush = new Drawing.SolidBrush(e.Item.Pressed ? palette.Pressed : palette.Hover);
            e.Graphics.FillPath(brush, path);
            e.Graphics.SmoothingMode = previousMode;
        }

        protected override void OnRenderItemImage(Forms.ToolStripItemImageRenderEventArgs e)
        {
            if (e.Image is null)
            {
                return;
            }

            var y = (e.Item.Height - TrayMenuIconSize) / 2;
            var x = (TrayMenuTextLeft - TrayMenuIconSize) / 2;
            var bounds = new Drawing.Rectangle(x, y, TrayMenuIconSize, TrayMenuIconSize);
            e.Graphics.DrawImage(e.Image, bounds);
        }

        protected override void OnRenderSeparator(Forms.ToolStripSeparatorRenderEventArgs e)
        {
            if (e.ToolStrip is null)
            {
                return;
            }

            var y = e.Item.Bounds.Height / 2;
            using var pen = new Drawing.Pen(palette.Separator);
            e.Graphics.DrawLine(pen, 38, y, e.ToolStrip.Width - 8, y);
        }

        protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
        {
            var textBounds = new Drawing.Rectangle(
                TrayMenuTextLeft,
                0,
                Math.Max(0, e.Item.Width - TrayMenuTextLeft - 16),
                e.Item.Height);
            Forms.TextRenderer.DrawText(
                e.Graphics,
                e.Text,
                e.TextFont,
                textBounds,
                palette.Text,
                Forms.TextFormatFlags.Left | Forms.TextFormatFlags.VerticalCenter | Forms.TextFormatFlags.SingleLine | Forms.TextFormatFlags.EndEllipsis | Forms.TextFormatFlags.NoPrefix);
        }
    }

    private sealed record TrayMenuPalette(
        Drawing.Color Background,
        Drawing.Color Text,
        Drawing.Color Icon,
        Drawing.Color Hover,
        Drawing.Color Pressed,
        Drawing.Color Border,
        Drawing.Color Separator)
    {
        public static TrayMenuPalette Create(bool dark)
        {
            return dark
                ? new TrayMenuPalette(
                    Drawing.Color.FromArgb(255, 32, 32, 32),
                    Drawing.Color.FromArgb(255, 243, 243, 243),
                    Drawing.Color.FromArgb(255, 96, 205, 255),
                    Drawing.Color.FromArgb(255, 54, 54, 54),
                    Drawing.Color.FromArgb(255, 62, 62, 62),
                    Drawing.Color.FromArgb(255, 69, 69, 69),
                    Drawing.Color.FromArgb(255, 62, 62, 62))
                : new TrayMenuPalette(
                    Drawing.Color.FromArgb(255, 249, 249, 249),
                    Drawing.Color.FromArgb(255, 32, 32, 32),
                    Drawing.Color.FromArgb(255, 0, 95, 184),
                    Drawing.Color.FromArgb(255, 238, 238, 238),
                    Drawing.Color.FromArgb(255, 229, 229, 229),
                    Drawing.Color.FromArgb(255, 218, 218, 218),
                    Drawing.Color.FromArgb(255, 225, 225, 225));
        }
    }

    private static Drawing2D.GraphicsPath CreateRoundedRectangle(Drawing.Rectangle bounds, int radius)
    {
        var diameter = radius * 2;
        var path = new Drawing2D.GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private void ShowQuickMenu()
    {
        if (_pasteTargetWindow == IntPtr.Zero)
        {
            _pasteTargetWindow = NativeMethods.GetForegroundWindow();
        }

        CaptureClipboard();

        var menuItems = new List<QuickMenuItem>();
        var pinnedIds = Settings.PinnedHistoryIds.ToHashSet(StringComparer.Ordinal);
        var historyItems = History.Items.Where(item => !pinnedIds.Contains(item.Id)).ToArray();
        var pinnedItems = Settings.PinnedHistoryIds
            .Select(History.Find)
            .OfType<ClipboardSnapshot>()
            .ToArray();
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

        if (pinnedItems.Length > 0)
        {
            menuItems.Add(new QuickMenuItem(
                Translate("PinnedHistory"),
                $"{pinnedItems.Length} items",
                ">",
                "Enter",
                () => { },
                LazyChildren: () => pinnedItems.Select(CreateHistoryMenuItem).ToArray(),
                IconGlyph: "\uE718",
                IconFontFamily: "Segoe Fluent Icons"));
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
            Settings.QuickMenuImagePreviewSize,
            Settings.QuickMenuShowCapturedAt,
            Settings.QuickMenuShowShortcutHints,
            Settings.QuickMenuShortcuts,
            ShowHistoryWindow,
            Translate("Search"),
            Translate("SearchPrompt"),
            Translate("Search"),
            Translate("AdvancedSearch"),
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

    private void MarkInitialLaunchCompleted()
    {
        if (Settings.InitialLaunchCompleted)
        {
            return;
        }

        Settings.InitialLaunchCompleted = true;
        SaveSettings();
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

    private static string NormalizeQuickMenuImagePreviewSize(string? size)
    {
        return size?.ToLowerInvariant() switch
        {
            "none" or "small" or "large" => size.ToLowerInvariant(),
            _ => "medium"
        };
    }

    private static int NormalizeClipboardCaptureDelay(int milliseconds)
    {
        return milliseconds is 0 or 50 or 100 or 150 or 250 or 500 or 1000
            ? milliseconds
            : 150;
    }

    private static string NormalizeQuickMenuShortcut(string? shortcut, string fallback, IReadOnlyCollection<string> allowed)
    {
        if (string.IsNullOrWhiteSpace(shortcut))
        {
            return fallback;
        }

        var parts = shortcut.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return fallback;
        }

        if (parts.Take(parts.Length - 1).Any(part => !part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)
            && !part.Equals("Control", StringComparison.OrdinalIgnoreCase)))
        {
            return fallback;
        }

        var control = parts.Take(parts.Length - 1).Any(part => part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)
            || part.Equals("Control", StringComparison.OrdinalIgnoreCase));
        var key = parts[^1].ToUpperInvariant();
        var normalized = control ? $"Ctrl+{key}" : key;
        return allowed.FirstOrDefault(item => item.Equals(normalized, StringComparison.OrdinalIgnoreCase)) ?? fallback;
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

    private static T ReadExportFile<T>(string path)
    {
        return JsonSerializer.Deserialize<T>(File.ReadAllText(path), ExportJsonOptions)
            ?? throw new InvalidOperationException("The selected file is not a supported Clipton export.");
    }

    private static void WriteExportFile<T>(string path, T value)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(value, ExportJsonOptions));
    }

    private static readonly JsonSerializerOptions ExportJsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private sealed record HistoryExportDto(int Version, DateTimeOffset ExportedAt, HistoryExportItemDto[] Items);

    private sealed record HistoryExportItemDto(
        string Id,
        DateTimeOffset CapturedAt,
        ClipboardFormatKind[] Formats,
        string? Text,
        string? Rtf,
        string? Html,
        byte[]? ImagePng,
        string[] FilePaths)
    {
        public static HistoryExportItemDto FromSnapshot(ClipboardSnapshot snapshot)
        {
            return new HistoryExportItemDto(
                snapshot.Id,
                snapshot.CapturedAt,
                snapshot.Formats.ToArray(),
                snapshot.Text,
                snapshot.Rtf,
                snapshot.Html,
                snapshot.ImagePng,
                snapshot.FilePaths.ToArray());
        }

        public ClipboardSnapshot ToSnapshot()
        {
            return new ClipboardSnapshot(
                string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString("N") : Id,
                CapturedAt == default ? DateTimeOffset.UtcNow : CapturedAt,
                Formats is { Length: > 0 } ? Formats : InferFormats(),
                Text,
                Rtf,
                Html,
                ImagePng,
                FilePaths);
        }

        private ClipboardFormatKind[] InferFormats()
        {
            var formats = new List<ClipboardFormatKind>();
            if (!string.IsNullOrEmpty(Text))
            {
                formats.Add(ClipboardFormatKind.Text);
            }

            if (!string.IsNullOrEmpty(Rtf))
            {
                formats.Add(ClipboardFormatKind.RichText);
            }

            if (!string.IsNullOrEmpty(Html))
            {
                formats.Add(ClipboardFormatKind.Html);
            }

            if (ImagePng is { Length: > 0 })
            {
                formats.Add(ClipboardFormatKind.Image);
            }

            if (FilePaths is { Length: > 0 })
            {
                formats.Add(ClipboardFormatKind.FileDrop);
            }

            return formats.Count > 0 ? formats.ToArray() : [ClipboardFormatKind.Text];
        }
    }

    private sealed record SnippetExportDto(int Version, DateTimeOffset ExportedAt, Snippet[] Items);

    private void SendPaste()
    {
        RestorePasteTarget();
        Thread.Sleep(60);
        NativeMethods.keybd_event(NativeMethods.VkControl, 0, 0, UIntPtr.Zero);
        NativeMethods.keybd_event(NativeMethods.VkV, 0, 0, UIntPtr.Zero);
        Thread.Sleep(20);
        NativeMethods.keybd_event(NativeMethods.VkV, 0, NativeMethods.KeyeventfKeyup, UIntPtr.Zero);
        NativeMethods.keybd_event(NativeMethods.VkControl, 0, NativeMethods.KeyeventfKeyup, UIntPtr.Zero);
    }

    private void RestorePasteTarget()
    {
        if (_pasteTargetWindow == IntPtr.Zero)
        {
            return;
        }

        var currentThread = NativeMethods.GetCurrentThreadId();
        var targetThread = NativeMethods.GetWindowThreadProcessId(_pasteTargetWindow, out _);
        var foreground = NativeMethods.GetForegroundWindow();
        var foregroundThread = foreground == IntPtr.Zero
            ? 0
            : NativeMethods.GetWindowThreadProcessId(foreground, out _);

        var attachedTarget = targetThread != 0 && targetThread != currentThread && NativeMethods.AttachThreadInput(currentThread, targetThread, true);
        var attachedForeground = foregroundThread != 0
            && foregroundThread != currentThread
            && foregroundThread != targetThread
            && NativeMethods.AttachThreadInput(currentThread, foregroundThread, true);

        try
        {
            NativeMethods.BringWindowToTop(_pasteTargetWindow);
            NativeMethods.SetForegroundWindow(_pasteTargetWindow);
            NativeMethods.SetActiveWindow(_pasteTargetWindow);
            NativeMethods.SetFocus(_pasteTargetWindow);
            Thread.Sleep(160);
        }
        finally
        {
            if (attachedForeground)
            {
                NativeMethods.AttachThreadInput(currentThread, foregroundThread, false);
            }

            if (attachedTarget)
            {
                NativeMethods.AttachThreadInput(currentThread, targetThread, false);
            }
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
            : CreateTextPasteOptions(plainText, item.Id);
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
            CapturedAt: item.CapturedAt,
            IsPinned: IsHistoryPinned(item.Id),
            Formats: item.Formats);
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

    private string? SaveHistoryImagePreview(ClipboardSnapshot item)
    {
        if (item.ImagePng is not { Length: > 0 })
        {
            return null;
        }

        try
        {
            Directory.CreateDirectory(_thumbnailPath);
            var path = Path.Combine(_thumbnailPath, $"{item.Id}-preview.png");
            if (!File.Exists(path))
            {
                File.WriteAllBytes(path, item.ImagePng);
            }

            return path;
        }
        catch
        {
            return null;
        }
    }

    private void DeleteHistoryImageFiles(string id)
    {
        if (!Directory.Exists(_thumbnailPath))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(_thumbnailPath, $"{id}-*.png"))
        {
            TryDeleteFile(file);
        }
    }

    private void ClearHistoryImageFiles()
    {
        if (!Directory.Exists(_thumbnailPath))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(_thumbnailPath, "*.png"))
        {
            TryDeleteFile(file);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
        }
    }

    public HistoryItemViewModel CreateHistoryItemViewModel(ClipboardSnapshot snapshot)
    {
        var formats = CreateFormatSummary(snapshot.Formats);
        var plainText = ClipboardBridge.GetPlainText(snapshot);
        var thumbnailPath = SaveHistoryThumbnail(snapshot);
        var previewImagePath = SaveHistoryImagePreview(snapshot);
        var isImage = snapshot.Formats.Contains(ClipboardFormatKind.Image);
        var snippet = Snippets.FindByText(plainText);
        if (snippet is not null)
        {
            return new HistoryItemViewModel(snapshot.Id, snippet.DisplayName, $"{Translate("RegisteredSnippetMasked")} - {formats}", snapshot.CapturedAt, isImage, thumbnailPath, previewImagePath);
        }

        if (Settings.MaskSensitiveContent && CreateMaskedPreview(plainText) is { } maskedPreview)
        {
            return new HistoryItemViewModel(snapshot.Id, NormalizePreviewText(maskedPreview), $"{Translate("MaskedSensitive")} - {formats}", snapshot.CapturedAt, isImage, thumbnailPath, previewImagePath);
        }

        return new HistoryItemViewModel(snapshot.Id, CreatePreviewText(snapshot, plainText), formats, snapshot.CapturedAt, isImage, thumbnailPath, previewImagePath);
    }

    private bool IsMaskedHistoryItem(ClipboardSnapshot snapshot)
    {
        var plainText = ClipboardBridge.GetPlainText(snapshot);
        return Snippets.FindByText(plainText) is not null
            || (Settings.MaskSensitiveContent && CreateMaskedPreview(plainText) is not null);
    }

    private MaskedHistoryKind GetMaskedHistoryKind(ClipboardSnapshot snapshot)
    {
        var plainText = ClipboardBridge.GetPlainText(snapshot);
        if (Snippets.FindByText(plainText) is not null)
        {
            return MaskedHistoryKind.RegisteredSnippet;
        }

        return Settings.MaskSensitiveContent && CreateMaskedPreview(plainText) is not null
            ? MaskedHistoryKind.Sensitive
            : MaskedHistoryKind.None;
    }

    private string? CreateMaskedPreview(string? plainText)
    {
        return SensitiveContentDetector.CreateMaskedPreview(
            plainText,
            Settings.MaskVisiblePrefixLength,
            Settings.CustomMaskPatterns);
    }

    private string CreatePreviewText(ClipboardSnapshot snapshot, string? plainText)
    {
        if (!string.IsNullOrWhiteSpace(plainText))
        {
            return NormalizePreviewText(plainText);
        }

        if (snapshot.FilePaths.Count > 0)
        {
            return string.Join(", ", snapshot.FilePaths.Select(Path.GetFileName));
        }

        if (snapshot.ImagePng is { Length: > 0 })
        {
            return Translate("Image");
        }

        if (!string.IsNullOrWhiteSpace(snapshot.Rtf))
        {
            return Translate("FormatRichText");
        }

        if (!string.IsNullOrWhiteSpace(snapshot.Html))
        {
            return Translate("FormatHtml");
        }

        return Translate("ClipboardItem");
    }

    private string CreateFormatSummary(IEnumerable<ClipboardFormatKind> formats)
    {
        return string.Join(", ", formats.Select(TranslateFormat));
    }

    private string TranslateFormat(ClipboardFormatKind format)
    {
        return format switch
        {
            ClipboardFormatKind.Text => Translate("FormatText"),
            ClipboardFormatKind.RichText => Translate("FormatRichText"),
            ClipboardFormatKind.Html => Translate("FormatHtml"),
            ClipboardFormatKind.Image => Translate("Image"),
            ClipboardFormatKind.FileDrop => Translate("FormatFileDrop"),
            _ => format.ToString()
        };
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
            PlainTextInvoke: () => PasteSnippet(snippet.Folder, snippet.Name),
            PasteOptions: CreateTextPasteOptions(() => SnippetTemplateRenderer.Render(snippet.Text)),
            IconGlyph: "S");
    }

    private IReadOnlyList<QuickMenuPasteOption> CreateTextPasteOptions(string? text, string? historyId = null)
    {
        return CreateTextPasteOptions(() => text, historyId);
    }

    private IReadOnlyList<QuickMenuPasteOption> CreateTextPasteOptions(Func<string?> textFactory, string? historyId = null)
    {
        var text = textFactory();
        if (string.IsNullOrEmpty(text))
        {
            return historyId is null
                ? []
                : [CreatePinPasteOption(historyId)];
        }

        var options = new List<QuickMenuPasteOption>
        {
            new QuickMenuPasteOption(Translate("PastePlain"), "\uE8D2", () => PasteText(textFactory() ?? string.Empty)),
            new QuickMenuPasteOption(Translate("PasteNoLineBreaks"), "\uE8EE", () => PasteText(RemoveLineBreaks(textFactory() ?? string.Empty))),
            new QuickMenuPasteOption(Translate("PasteUppercase"), "AA", () => PasteText((textFactory() ?? string.Empty).ToUpperInvariant()), "Segoe UI"),
            new QuickMenuPasteOption(Translate("PasteLowercase"), "aa", () => PasteText((textFactory() ?? string.Empty).ToLowerInvariant()), "Segoe UI"),
            new QuickMenuPasteOption(Translate("PasteTrimmed"), "\uE8C6", () => PasteText((textFactory() ?? string.Empty).Trim())),
            new QuickMenuPasteOption(Translate("PasteJsonString"), "{ }", () => PasteText(InferredJsonFormatter.Format(textFactory() ?? string.Empty)), "Segoe UI")
        };

        var urls = ExtractUrls(text);
        if (urls.Length > 0)
        {
            options.Add(new QuickMenuPasteOption(Translate("PasteExtractUrls"), "\uE71B", () => PasteText(string.Join(Environment.NewLine, urls))));
        }

        if (TryFormatJson(text) is { } formattedJson)
        {
            options.Add(new QuickMenuPasteOption(Translate("PasteFormattedJson"), "{ }", () => PasteText(formattedJson), "Segoe UI"));
        }

        if (historyId is not null)
        {
            options.Add(CreatePinPasteOption(historyId));
        }

        return options;
    }

    private QuickMenuPasteOption CreatePinPasteOption(string historyId)
    {
        return IsHistoryPinned(historyId)
            ? new QuickMenuPasteOption(Translate("UnpinHistory"), "\uE77A", () => TogglePinnedHistoryItem(historyId))
            : new QuickMenuPasteOption(Translate("PinHistory"), "\uE718", () => TogglePinnedHistoryItem(historyId));
    }

    public static string[] ExtractUrls(string text)
    {
        return UrlRegex.Matches(text)
            .Select(match => match.Value.TrimEnd('.', ',', ';', ':', '!', '?', ')', ']'))
            .Where(url => url.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? TryFormatJson(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            return JsonSerializer.Serialize(document.RootElement, ExportJsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
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
            new QuickMenuPasteOption(Translate("CopyImageOnly"), "\uE8C8", () => PasteImage(item.Id, ImagePasteMode.Png, sendPaste: false)),
            CreatePinPasteOption(item.Id)
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
