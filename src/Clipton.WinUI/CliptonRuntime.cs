using System.Text.Json;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using Clipton.Core;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Win32;
using Windows.Graphics;
using Windows.UI.ViewManagement;
using WinRT.Interop;
using Drawing = System.Drawing;
using Drawing2D = System.Drawing.Drawing2D;
using Imaging = System.Drawing.Imaging;

namespace Clipton.WinUI;

/// <summary>
/// Coordinates Clipton's app lifetime, settings, clipboard capture, history and paste actions.
/// </summary>
/// <remarks>
/// UI windows stay thin and call into this runtime for stateful operations. The runtime
/// owns persistence and OS integrations so behavior remains consistent between the tray,
/// settings window and quick menu surfaces.
/// </remarks>
public sealed class CliptonRuntime : IDisposable
{
    // Only a small prefix of persisted history is loaded at startup. Older items stay on
    // disk and are paged in when the UI asks for more, keeping startup and memory bounded.
    private const int HistorySaveDebounceMilliseconds = 500;
    private const int InitialPersistedHistoryLoadCount = 10;
    private const int ResidentHistoryFlushOverflowCount = 2;
    private const int ImmediateHistorySaveMinIntervalMilliseconds = 1000;
    private const int MaxCachedHistoryImages = 64;
    private const int QuickMenuHotkeyDebounceMilliseconds = 160;
    private const int TempPasteMaxFiles = 25;
    private const string HistoryExportKind = "history";
    private const string SnippetExportKind = "snippets";
    private const int QuickMenuClipboardCaptureTimeoutMilliseconds = 500;
    private const int HistoryAccessUnlockMaxFailedAttempts = 5;
    private const string PreviewLineBreakMarker = " \u21B5 ";
    private static readonly TimeSpan DispatcherMarshalTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan TempPasteMaxAge = TimeSpan.FromHours(1);
    private static readonly TimeSpan TempPasteDeleteDelay = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan TempPasteDeleteAfterPasteDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan HistoryAccessUnlockBackoff = TimeSpan.FromSeconds(30);
    private static readonly Regex UrlRegex = new(@"\b(?:https?|ftp)://[^\s<>()""']+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private readonly DispatcherQueue? _dispatcherQueue;
    private readonly AccessibilitySettings? _accessibilitySettings;
    private readonly LocalizationCatalog _localization = new();
    private readonly JsonSettingsStore _settingsStore;
    private readonly EncryptedHistoryStore _historyStore;
    private readonly string _snippetPath;
    private readonly string _legacySnippetPath;
    private readonly string _thumbnailPath;
    private readonly string _tempPastePath;
    private readonly object _historySaveGate = new();
    private readonly object _historyPersistChainGate = new();
    private readonly ClipboardCaptureWorker _captureWorker = new();
    private Task<bool> _historyPersistChain = Task.FromResult(true);
    private readonly object _historyImageCacheGate = new();
    private readonly Dictionary<string, byte[]> _historyImageBytesByKey = new(StringComparer.Ordinal);
    private readonly Queue<string> _historyImageCacheKeys = new();
    private readonly HashSet<string> _deletedHistoryIds = new(StringComparer.Ordinal);
    private CancellationTokenSource? _historySaveDebounce;
    private long _historySaveVersion;
    private long _lastImmediateHistorySaveTick;
    private int _persistedHistoryCount;
    private int _loadedPersistedHistoryCount;
    private HotkeyMessageWindow? _messageWindow;
    private NativeTrayIcon? _notifyIcon;
    private Window? _lifetimeWindow;
    private MainWindow? _mainWindow;
    private QuickMenuWindow? _defaultQuickMenu;
    private RichQuickMenuWindow? _richQuickMenu;
    private PasteTargetWindowIdentity _pasteTargetWindow;
    private long _lastQuickMenuRequestTick;
    private int _quickMenuRequestPending;
    private bool _clipboardServicesStarted;
    private uint _lastCapturedClipboardSequence;
    private CancellationTokenSource? _clipboardCaptureDelay;
    private DispatcherQueueTimer? _historyAccessLockTimer;
    private DateTimeOffset? _historyAccessUnlockedUntilUtc;
    private int _historyAccessUnlockFailedAttempts;
    private DateTimeOffset? _historyAccessUnlockBlockedUntilUtc;
    private int _disposeState;

    /// <summary>
    /// Creates the runtime and loads settings, snippets and the initial resident history.
    /// </summary>
    /// <param name="dataDirectory">Optional test or custom data directory.</param>
    /// <param name="isSafeMode">When true, disables startup-registration changes.</param>
    public CliptonRuntime(string? dataDirectory = null, bool isSafeMode = false)
    {
        _dispatcherQueue = ResolveDispatcherQueue(isSafeMode);
        var appData = string.IsNullOrWhiteSpace(dataDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Clipton")
            : dataDirectory;
        _settingsStore = new JsonSettingsStore(Path.Combine(appData, "settings.json"));
        _historyStore = new EncryptedHistoryStore(Path.Combine(appData, "history.dat"));
        _snippetPath = Path.Combine(appData, "snippets.dat");
        _legacySnippetPath = Path.Combine(appData, "snippets.json");
        _thumbnailPath = Path.Combine(appData, "thumbs");
        _tempPastePath = string.IsNullOrWhiteSpace(dataDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Clipton", "TempPaste")
            : Path.Combine(dataDirectory, "TempPaste");
        DataDirectory = appData;
        IsSafeMode = isSafeMode;
        Settings = _settingsStore.Load();
        AppDiagnostics.Configure(Settings.DiagnosticLoggingEnabled || AppProfiler.Enabled);
        AppProfiler.Mark("Settings loaded.");
        History = new ClipboardHistory(Settings.MaxHistoryItems);
        Snippets = LoadSnippets(_snippetPath, _legacySnippetPath, out var loadedLegacySnippets);
        if (loadedLegacySnippets)
        {
            SaveSnippets(_snippetPath, Snippets);
            TryDeleteFile(_legacySnippetPath);
        }
        AppProfiler.Mark("Runtime stores initialized.");

        if (Settings.PersistEncryptedHistory)
        {
            _persistedHistoryCount = _historyStore.Count();
            var snapshots = _historyStore.LoadRecent(Math.Min(Settings.MaxHistoryItems, InitialPersistedHistoryLoadCount));
            _loadedPersistedHistoryCount = snapshots.Count;
            foreach (var snapshot in snapshots.Reverse())
            {
                History.Add(ApplySourceMetadataPolicy(snapshot));
            }
        }

        AppProfiler.Mark($"History loaded. count={History.Items.Count}; persisted={_persistedHistoryCount}");
        _accessibilitySettings = isSafeMode ? null : TryCreateAccessibilitySettings();
        if (_accessibilitySettings is not null)
        {
            try
            {
                _accessibilitySettings.HighContrastChanged += OnHighContrastChanged;
            }
            catch (COMException exception)
            {
                AppDiagnostics.Log(exception, "High contrast event subscription");
                _accessibilitySettings = null;
            }
        }
    }

    private static AccessibilitySettings? TryCreateAccessibilitySettings()
    {
        try
        {
            return new AccessibilitySettings();
        }
        catch (Exception exception) when (exception is COMException or TypeInitializationException)
        {
            AppDiagnostics.Log(exception, "Accessibility settings initialization");
            return null;
        }
    }

    private static DispatcherQueue? ResolveDispatcherQueue(bool isSafeMode)
    {
        try
        {
            return DispatcherQueue.GetForCurrentThread();
        }
        catch (COMException) when (isSafeMode)
        {
            return null;
        }
    }

    /// <summary>Current normalized settings model.</summary>
    public CliptonSettings Settings { get; }

    /// <summary>Directory that owns settings, snippets, history and generated assets.</summary>
    public string DataDirectory { get; }

    /// <summary>Default app-data directory used when no custom directory is configured.</summary>
    public string DefaultDataDirectory => AppDataDirectorySettings.DefaultDirectory;

    /// <summary>Configured app-data directory to use after restart, when present.</summary>
    public string? ConfiguredDataDirectory => AppDataDirectorySettings.LoadConfiguredDirectory();

    /// <summary>True when operations with external side effects should be reduced.</summary>
    public bool IsSafeMode { get; }

    /// <summary>Resident clipboard history prefix.</summary>
    public ClipboardHistory History { get; }

    /// <summary>In-memory snippet catalog.</summary>
    public SnippetCatalog Snippets { get; }

    /// <summary>Count of persisted history items that are not currently resident.</summary>
    public int UnloadedPersistedHistoryCount => Math.Max(0, _persistedHistoryCount - _loadedPersistedHistoryCount);

    /// <summary>Total history count available to the UI under the current capacity.</summary>
    public int AvailableHistoryCount => Math.Min(Settings.MaxHistoryItems, History.Items.Count + UnloadedPersistedHistoryCount);

    /// <summary>Locale after resolving the system setting.</summary>
    public string EffectiveLocale => ResolveLocale(Settings.Locale);

    /// <summary>Theme after resolving the system setting.</summary>
    public string EffectiveTheme => ResolveTheme(Settings.Theme);

    /// <summary>Translates a UI text key for the effective locale.</summary>
    public string Translate(string key) => _localization.Translate(EffectiveLocale, key);

    /// <summary>Product version displayed in settings.</summary>
    public string AppVersion => GetAppVersion();

    /// <summary>Packaging/runtime status displayed in settings.</summary>
    public string PackageStatus => GetPackageStatus();

    /// <summary>True once shutdown has started.</summary>
    public bool IsExiting { get; private set; }

    /// <summary>Raised when Windows reports a High Contrast setting change.</summary>
    public event EventHandler? HighContrastChanged;

    /// <summary>Current High Contrast state, or false when the Windows API is unavailable.</summary>
    public bool IsHighContrast
    {
        get
        {
            try
            {
                return _accessibilitySettings?.HighContrast ?? false;
            }
            catch (COMException exception)
            {
                AppDiagnostics.Log(exception, "Read high contrast state");
                return false;
            }
        }
    }

    public bool IsHistoryAccessLockConfigured =>
        HistoryAccessLockCredential.HasCredential(Settings.HistoryAccessLockPinSalt, Settings.HistoryAccessLockPinHash);

    public bool IsHistoryAccessLockEnabled => Settings.HistoryAccessLockEnabled && IsHistoryAccessLockConfigured;

    public bool RequiresHistoryAccessUnlock => IsHistoryAccessLockEnabled && !IsHistoryAccessUnlocked();

    internal bool IsHistoryAccessUnlockBackoffActive =>
        _historyAccessUnlockBlockedUntilUtc is { } blockedUntil && blockedUntil > DateTimeOffset.UtcNow;

    /// <summary>
    /// Starts tray, lifetime window and clipboard services when onboarding is complete.
    /// </summary>
    public void Start()
    {
        EnsureDefaultSnippets();
        AppProfiler.Mark("Default snippets ensured.");
        CleanupTempPasteFiles();
        QuickMenuWindow.CleanupImagePreviewTempFiles();
        AppProfiler.Mark("Temporary paste files cleaned.");
        CreateTrayIcon();
        AppProfiler.Mark("Tray icon created.");
        CreateLifetimeWindow();
        AppProfiler.Mark("Lifetime window created.");
        AppDiagnostics.Info("Runtime", "Clipton runtime started.");
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

    public void ActivateFromSecondInstance()
    {
        AppDiagnostics.Info("Runtime", "Second instance launch detected; activating settings window.");
        _dispatcherQueue?.TryEnqueue(ShowMainWindow);
    }

    public void ShowMainWindow()
    {
        var mainWindow = EnsureMainWindow();
        mainWindow.RefreshTexts();
        AppProfiler.Mark("Main window text refreshed.");
        mainWindow.RefreshItems();
        AppProfiler.Mark("Main window items refreshed.");
        if (RequiresHistoryAccessUnlock)
        {
            mainWindow.HideHistoryPageForLock();
        }

        mainWindow.ShowSettingsWindow();
        AppProfiler.Mark("Settings window shown.");
    }

    private MainWindow EnsureMainWindow()
    {
        if (_mainWindow is null)
        {
            _mainWindow = new MainWindow(this);
            AppProfiler.Mark("Main window constructed.");
        }

        return _mainWindow;
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
        if (RequiresHistoryAccessUnlock)
        {
            return;
        }

        var item = History.Find(id);
        if (item is null)
        {
            return;
        }

        var pasteAsPlainText = asPlainText || Settings.PastePlainTextByDefault;
        QueueClipboardPaste(() => ClipboardBridge.Put(item, pasteAsPlainText), sendPaste: true);
    }

    public void PasteSnippet(string folder, string name)
    {
        if (RequiresHistoryAccessUnlock)
        {
            return;
        }

        var snippet = Snippets.Find(folder, name);
        if (snippet is null)
        {
            return;
        }

        var snippetText = snippet.Text;
        var requiresFilePaths = SnippetTemplateRenderer.RequiresFilePaths(snippetText);
        QueueClipboardPaste(() =>
        {
            var filePaths = requiresFilePaths ? GetCurrentClipboardFilePaths() : null;
            return ClipboardBridge.PutText(SnippetTemplateRenderer.Render(snippetText, filePaths: filePaths));
        }, sendPaste: true);
    }

    public void PasteText(string text)
    {
        if (RequiresHistoryAccessUnlock)
        {
            return;
        }

        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        QueueClipboardPaste(() => ClipboardBridge.PutText(text), sendPaste: true);
    }

    public void QuickEditAndPasteText(string text, string title)
    {
        if (RequiresHistoryAccessUnlock)
        {
            return;
        }

        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        _ = QuickEditAndPasteTextAsync(text, title);
    }

    public void PreviewText(string text, string title)
    {
        if (RequiresHistoryAccessUnlock)
        {
            return;
        }

        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        _ = QuickTextWindow.ShowAsync(
            title,
            text,
            EffectiveTheme,
            editable: false,
            Translate("PasteEdited"),
            Translate("Close"));
    }

    private async Task QuickEditAndPasteTextAsync(string text, string title)
    {
        var edited = await QuickTextWindow.ShowAsync(
            title,
            text,
            EffectiveTheme,
            editable: true,
            Translate("PasteEdited"),
            Translate("Cancel"));
        if (edited is not null)
        {
            PasteText(edited);
        }
    }

    public void PasteImage(string id, ImagePasteMode mode, bool sendPaste)
    {
        if (RequiresHistoryAccessUnlock)
        {
            return;
        }

        var item = History.Find(id);
        if (item?.ImagePng is not { Length: > 0 } imagePng)
        {
            return;
        }

        string? tempPasteFilePath = null;
        Func<bool>? writeClipboard = mode switch
        {
            ImagePasteMode.Original => () => ClipboardBridge.Put(item, asPlainText: false),
            ImagePasteMode.Png => () => ClipboardBridge.PutImagePng(imagePng),
            ImagePasteMode.Jpeg => () => ClipboardBridge.PutImageJpeg(imagePng),
            ImagePasteMode.ResizedHalf => () => ClipboardBridge.PutImagePng(ResizeImagePng(imagePng, 0.5)),
            ImagePasteMode.File => () =>
            {
                tempPasteFilePath = CreateTempImageFile(item);
                return ClipboardBridge.PutFileDrop(tempPasteFilePath);
            },
            _ => null
        };
        if (writeClipboard is null)
        {
            return;
        }

        QueueClipboardPaste(writeClipboard, sendPaste, () => tempPasteFilePath);
    }

    private void QueueClipboardPaste(Func<bool> writeClipboard, bool sendPaste, Func<string?>? tempPasteFilePath = null)
    {
        var pasteTargetWindow = _pasteTargetWindow;
        _pasteTargetWindow = default;
        _captureWorker.Post(() =>
        {
            try
            {
                var clipboardUpdated = writeClipboard();
                if (clipboardUpdated && sendPaste)
                {
                    SendPaste(pasteTargetWindow);
                }
            }
            finally
            {
                if (tempPasteFilePath?.Invoke() is { } path)
                {
                    ScheduleTempPasteFileDeletion(path, TempPasteDeleteAfterPasteDelay);
                }
            }
        });
    }

    public IReadOnlyList<QuickMenuPasteOption> CreateHistoryContextOptions(string id)
    {
        if (RequiresHistoryAccessUnlock)
        {
            return [];
        }

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
            new(Translate("PasteOriginal"), "\uE77F", () => PasteHistoryItem(item.Id, asPlainText: false), Id: QuickMenuPasteOptionIds.PasteOriginal)
        };

        options.AddRange(CreateTextPasteOptions(plainText, item.Id));
        return EnabledPasteOptions(options);
    }

    public async Task SetStartWithWindowsAsync(bool enabled)
    {
        if (IsSafeMode)
        {
            Settings.StartWithWindows = false;
            SaveSettings();
            return;
        }

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

    public void ConfigureHistoryAccessLock(string pin, int timeoutMinutes)
    {
        var credential = HistoryAccessLockCredential.Create(pin);
        Settings.HistoryAccessLockPinSalt = credential.Salt;
        Settings.HistoryAccessLockPinHash = credential.Hash;
        Settings.HistoryAccessLockTimeoutMinutes = HistoryAccessLockCredential.NormalizeTimeoutMinutes(timeoutMinutes);
        Settings.HistoryAccessLockEnabled = true;
        SaveSettings();
        ClearHistoryAccessUnlockFailures();
        MarkHistoryAccessUnlocked();
    }

    public void SetHistoryAccessLockEnabled(bool enabled)
    {
        Settings.HistoryAccessLockEnabled = enabled && IsHistoryAccessLockConfigured;
        SaveSettings();
        if (Settings.HistoryAccessLockEnabled)
        {
            LockHistoryAccess();
        }
        else
        {
            _historyAccessUnlockedUntilUtc = null;
            ClearHistoryAccessUnlockFailures();
            StopHistoryAccessLockTimer();
        }
    }

    public void SetHistoryAccessLockTimeoutMinutes(int minutes)
    {
        Settings.HistoryAccessLockTimeoutMinutes = HistoryAccessLockCredential.NormalizeTimeoutMinutes(minutes);
        SaveSettings();
        if (IsHistoryAccessUnlocked())
        {
            MarkHistoryAccessUnlocked();
        }
    }

    public bool UnlockHistoryAccess(string pin)
    {
        var now = DateTimeOffset.UtcNow;
        ClearExpiredHistoryAccessUnlockBackoff(now);
        if (_historyAccessUnlockBlockedUntilUtc is { } blockedUntil && blockedUntil > now)
        {
            return false;
        }

        if (!HistoryAccessLockCredential.Verify(pin, Settings.HistoryAccessLockPinSalt, Settings.HistoryAccessLockPinHash))
        {
            _historyAccessUnlockFailedAttempts++;
            if (_historyAccessUnlockFailedAttempts >= HistoryAccessUnlockMaxFailedAttempts)
            {
                _historyAccessUnlockBlockedUntilUtc = now.Add(HistoryAccessUnlockBackoff);
                AppDiagnostics.Info("HistoryAccessLock", "PIN unlock temporarily blocked after repeated failures.");
            }

            return false;
        }

        ClearHistoryAccessUnlockFailures();
        MarkHistoryAccessUnlocked();
        return true;
    }

    public void LockHistoryAccess()
    {
        _historyAccessUnlockedUntilUtc = null;
        StopHistoryAccessLockTimer();
        if (_mainWindow is { } window)
        {
            window.HideHistoryPageForLock();
            window.RefreshItems();
        }

        _defaultQuickMenu?.Dismiss();
        _richQuickMenu?.Dismiss();
    }

    public void ResetHistoryAccessLockAndClearHistory() => ResetHistoryAccessLockAndClearProtectedData();

    public void ResetHistoryAccessLockAndClearProtectedData()
    {
        Settings.HistoryAccessLockEnabled = false;
        Settings.HistoryAccessLockPinSalt = string.Empty;
        Settings.HistoryAccessLockPinHash = string.Empty;
        Settings.HistoryAccessLockTimeoutMinutes = HistoryAccessLockCredential.DefaultTimeoutMinutes;
        _historyAccessUnlockedUntilUtc = null;
        ClearHistoryAccessUnlockFailures();
        StopHistoryAccessLockTimer();
        _defaultQuickMenu?.Dismiss();
        _richQuickMenu?.Dismiss();
        ClearHistory();
        ClearSnippets();
    }

    public void RefreshHistoryAccessUnlockWindow()
    {
        if (IsHistoryAccessUnlocked())
        {
            MarkHistoryAccessUnlocked();
        }
    }

    private bool IsHistoryAccessUnlocked()
    {
        return _historyAccessUnlockedUntilUtc is { } until && until > DateTimeOffset.UtcNow;
    }

    private void ClearExpiredHistoryAccessUnlockBackoff(DateTimeOffset now)
    {
        if (_historyAccessUnlockBlockedUntilUtc is { } blockedUntil && blockedUntil <= now)
        {
            ClearHistoryAccessUnlockFailures();
        }
    }

    private void ClearHistoryAccessUnlockFailures()
    {
        _historyAccessUnlockFailedAttempts = 0;
        _historyAccessUnlockBlockedUntilUtc = null;
    }

    private void ThrowIfHistoryAccessLocked()
    {
        if (RequiresHistoryAccessUnlock)
        {
            throw new InvalidOperationException("History access is locked.");
        }
    }

    private void MarkHistoryAccessUnlocked()
    {
        var timeoutMinutes = HistoryAccessLockCredential.NormalizeTimeoutMinutes(Settings.HistoryAccessLockTimeoutMinutes);
        _historyAccessUnlockedUntilUtc = timeoutMinutes <= 0
            ? DateTimeOffset.UtcNow.AddSeconds(10)
            : DateTimeOffset.UtcNow.AddMinutes(timeoutMinutes);
        ScheduleHistoryAccessLockTimer();
    }

    private void ScheduleHistoryAccessLockTimer()
    {
        StopHistoryAccessLockTimer();
        if (!IsHistoryAccessLockEnabled || _historyAccessUnlockedUntilUtc is not { } unlockedUntil)
        {
            return;
        }

        var dispatcherQueue = _dispatcherQueue;
        if (dispatcherQueue is null)
        {
            return;
        }

        var interval = unlockedUntil - DateTimeOffset.UtcNow;
        if (interval <= TimeSpan.Zero)
        {
            LockHistoryAccess();
            return;
        }

        var timer = dispatcherQueue.CreateTimer();
        _historyAccessLockTimer = timer;
        timer.IsRepeating = false;
        timer.Interval = interval;
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (!ReferenceEquals(_historyAccessLockTimer, timer))
            {
                return;
            }

            _historyAccessLockTimer = null;
            if (IsExiting || !IsHistoryAccessLockEnabled)
            {
                return;
            }

            if (IsHistoryAccessUnlocked())
            {
                ScheduleHistoryAccessLockTimer();
                return;
            }

            LockHistoryAccess();
        };
        timer.Start();
    }

    private void StopHistoryAccessLockTimer()
    {
        _historyAccessLockTimer?.Stop();
        _historyAccessLockTimer = null;
    }

    public void SetPauseCapture(bool paused)
    {
        Settings.PauseCapture = paused;
        SaveSettings();
    }

    public void SetExcludedCaptureApplicationPatterns(IEnumerable<string>? patterns)
    {
        Settings.ExcludedCaptureApplicationPatterns = ApplicationExclusionList.Normalize(patterns);
        SaveSettings();
    }

    /// <summary>
    /// Enables or disables encrypted local history persistence.
    /// </summary>
    /// <remarks>
    /// Disabling persistence is also a user data deletion operation. The runtime clears
    /// resident history, persisted history, pinned ids, thumbnails and temp paste files so
    /// the setting means "do not keep history" rather than merely "stop future writes".
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
            _deletedHistoryIds.Clear();
            _persistedHistoryCount = 0;
            _loadedPersistedHistoryCount = 0;
            Settings.PinnedHistoryIds = [];
            SaveHistory();
            ClearHistoryImageFiles();
            CleanupTempPasteFiles(deleteAll: true);
            QuickMenuWindow.CleanupImagePreviewTempFiles(deleteAll: true);
            _mainWindow?.RefreshItems();
        }

        SaveSettings();
    }

    public void SetMaskSensitiveContent(bool enabled)
    {
        Settings.MaskSensitiveContent = enabled;
        SaveSettings();
    }

    public void SetSaveHistorySourceMetadata(bool enabled)
    {
        if (Settings.SaveHistorySourceMetadata == enabled)
        {
            return;
        }

        Settings.SaveHistorySourceMetadata = enabled;
        SaveSettings();
        if (!enabled)
        {
            StripResidentHistorySourceMetadata();
            _historyStore.ClearSourceMetadata();
            _mainWindow?.RefreshItems();
        }
    }

    public void SetMaskDefinitionOptions(
        int visiblePrefixLength,
        string[] customPatterns,
        MaskRuleSettings? rules = null,
        MaskRuleDefinition[]? ruleDefinitions = null)
    {
        Settings.MaskVisiblePrefixLength = Math.Clamp(visiblePrefixLength, 0, 12);
        Settings.MaskRuleDefinitions = MaskRuleDefinitionDefaults.Normalize(ruleDefinitions, rules);
        Settings.MaskRules = MaskRuleDefinitionDefaults.ToSettings(Settings.MaskRuleDefinitions);
        Settings.MaskRules.CustomPattern = rules?.CustomPattern ?? Settings.MaskRules.CustomPattern;
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

    public void SetDiagnosticLogging(bool enabled)
    {
        Settings.DiagnosticLoggingEnabled = enabled;
        SaveSettings();
        AppDiagnostics.Configure(enabled);
    }

    public void OpenDiagnosticLogDirectory()
    {
        AppDiagnostics.OpenLogDirectory();
    }

    public void OpenDataDirectory()
    {
        Directory.CreateDirectory(DataDirectory);
        _ = ExternalLauncher.OpenFolderAsync(DataDirectory);
    }

    public void SetConfiguredDataDirectory(string? path)
    {
        AppDataDirectorySettings.SaveConfiguredDirectory(path);
    }

    public void ClearDiagnosticLogs()
    {
        AppDiagnostics.ClearLogs();
    }

    public void SetFolderMode(bool enabled)
    {
        Settings.FolderMode = enabled;
        SaveSettings();
    }

    public void SetQuickMenuTopLevelHistoryItems(int count)
    {
        Settings.QuickMenuTopLevelHistoryItems = QuickMenuHistoryBuckets.NormalizeTopLevelHistoryItems(count);
        Settings.FolderMode = true;
        SaveSettings();
    }

    public void SetQuickMenuImagePreviewSize(string size)
    {
        Settings.QuickMenuImagePreviewSize = NormalizeQuickMenuImagePreviewSize(size);
        SaveSettings();
    }

    public void SetQuickMenuDisplayMode(string mode)
    {
        Settings.QuickMenuDisplayMode = NormalizeQuickMenuDisplayMode(mode);
        SaveSettings();
    }

    public void SetQuickMenuShowCapturedAt(bool enabled)
    {
        Settings.QuickMenuShowCapturedAt = enabled;
        SaveSettings();
        ApplyQuickMenuDisplayOptions();
    }

    public void SetQuickMenuShowShortcutHints(bool enabled)
    {
        Settings.QuickMenuShowShortcutHints = enabled;
        SaveSettings();
        ApplyQuickMenuDisplayOptions();
    }

    public bool IsQuickMenuPasteOptionEnabled(string optionId)
    {
        Settings.QuickMenuPasteOptions ??= new QuickMenuPasteOptionSettings();
        return Settings.QuickMenuPasteOptions.IsEnabled(optionId);
    }

    public void SetQuickMenuPasteOptionEnabled(string optionId, bool enabled)
    {
        if (!QuickMenuPasteOptionIds.All.Contains(optionId, StringComparer.Ordinal))
        {
            return;
        }

        Settings.QuickMenuPasteOptions ??= new QuickMenuPasteOptionSettings();
        var disabledIds = (Settings.QuickMenuPasteOptions.DisabledOptionIds ?? [])
            .Where(id => !string.Equals(id, optionId, StringComparison.Ordinal))
            .ToList();
        if (!enabled)
        {
            disabledIds.Add(optionId);
        }

        Settings.QuickMenuPasteOptions.DisabledOptionIds = QuickMenuPasteOptionSettings.NormalizeDisabledOptionIds(disabledIds);
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
                    ["Ctrl+M"]);
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
        if (RequiresHistoryAccessUnlock)
        {
            return;
        }

        if (History.Remove(id))
        {
            _deletedHistoryIds.Add(id);
            UnpinHistoryItem(id, refresh: false);
            DeleteHistoryImageFiles(id);
            QueueHistorySave();
            _mainWindow?.RefreshItems();
        }
    }

    public void TogglePinnedHistoryItem(string id)
    {
        if (RequiresHistoryAccessUnlock)
        {
            return;
        }

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

    /// <summary>
    /// Pages older persisted history into the resident history prefix.
    /// </summary>
    public void LoadMorePersistedHistory(int count = QuickMenuHistoryBuckets.BucketSize)
    {
        if (RequiresHistoryAccessUnlock)
        {
            return;
        }

        if (count <= 0)
        {
            return;
        }

        EnsurePersistedHistoryLoaded(_loadedPersistedHistoryCount + count);
    }

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
        ThrowIfHistoryAccessLocked();

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
        ThrowIfHistoryAccessLocked();

        if (Snippets.Remove(folder, name))
        {
            SaveSnippets(_snippetPath, Snippets);
            _mainWindow?.RefreshItems();
        }
    }

    /// <summary>
    /// Exports all available history items to a portable JSON file.
    /// </summary>
    public int ExportHistory(string path)
    {
        ThrowIfHistoryAccessLocked();

        var dto = CreateHistoryExportDto(out var count);
        WriteExportFile(path, dto);
        return count;
    }

    public int ExportHistoryEncrypted(string path, string passphrase)
    {
        ThrowIfHistoryAccessLocked();

        var dto = CreateHistoryExportDto(out var count);
        WriteEncryptedExportFile(path, HistoryExportKind, dto, passphrase);
        return count;
    }

    private HistoryExportDto CreateHistoryExportDto(out int count)
    {
        var snapshots = new List<ClipboardSnapshot>(Settings.MaxHistoryItems);
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var seenFingerprints = new HashSet<string>(StringComparer.Ordinal);

        void AddDistinct(ClipboardSnapshot snapshot)
        {
            if (snapshots.Count >= Settings.MaxHistoryItems)
            {
                return;
            }

            var sanitized = ApplySourceMetadataPolicy(snapshot);
            var fingerprint = ClipboardHistory.CreateFingerprint(sanitized);
            if (seenIds.Add(sanitized.Id) && seenFingerprints.Add(fingerprint))
            {
                snapshots.Add(sanitized);
            }
        }

        foreach (var snapshot in History.Items)
        {
            AddDistinct(snapshot);
        }

        if (Settings.PersistEncryptedHistory && snapshots.Count < Settings.MaxHistoryItems)
        {
            foreach (var snapshot in LoadAllPersistedHistoryForExport())
            {
                if (!_deletedHistoryIds.Contains(snapshot.Id))
                {
                    AddDistinct(snapshot);
                }
            }
        }

        var items = snapshots
            .Select(HistoryExportItemDto.FromSnapshot)
            .ToArray();
        count = items.Length;
        return new HistoryExportDto(1, DateTimeOffset.UtcNow, items);
    }

    private IReadOnlyList<ClipboardSnapshot> LoadAllPersistedHistoryForExport()
    {
        return _historyStore.LoadAllStrict();
    }

    /// <summary>
    /// Imports history JSON into the resident history, using normal history de-duplication.
    /// </summary>
    public int ImportHistory(string path)
    {
        ThrowIfHistoryAccessLocked();

        return ImportHistory(ReadExportFile<HistoryExportDto>(path));
    }

    public int ImportHistoryEncrypted(string path, string passphrase)
    {
        ThrowIfHistoryAccessLocked();

        return ImportHistory(ReadEncryptedExportFile<HistoryExportDto>(path, HistoryExportKind, passphrase));
    }

    private int ImportHistory(HistoryExportDto dto)
    {
        var items = dto.Items ?? throw new InvalidOperationException("The selected file does not contain history items.");
        var before = History.Items.Count;
        foreach (var item in items.Reverse())
        {
            History.Add(ApplySourceMetadataPolicy(item.ToSnapshot()));
        }

        SaveHistory();
        _persistedHistoryCount = _historyStore.Count();
        _loadedPersistedHistoryCount = Math.Min(History.Items.Count, _persistedHistoryCount);
        _mainWindow?.RefreshItems();
        return Math.Max(0, History.Items.Count - before);
    }

    /// <summary>
    /// Previews how a history import would affect the resident history and configured capacity.
    /// </summary>
    public HistoryImportPreview PreviewImportHistory(string path)
    {
        ThrowIfHistoryAccessLocked();

        return PreviewImportHistory(ReadExportFile<HistoryExportDto>(path));
    }

    public HistoryImportPreview PreviewImportHistoryEncrypted(string path, string passphrase)
    {
        ThrowIfHistoryAccessLocked();

        return PreviewImportHistory(ReadEncryptedExportFile<HistoryExportDto>(path, HistoryExportKind, passphrase));
    }

    private HistoryImportPreview PreviewImportHistory(HistoryExportDto dto)
    {
        var items = dto.Items ?? throw new InvalidOperationException("The selected file does not contain history items.");
        var importedFingerprints = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            importedFingerprints.Add(ClipboardHistory.CreateFingerprint(item.ToSnapshot()));
        }

        var currentFingerprints = History.Items
            .Select(ClipboardHistory.CreateFingerprint)
            .ToHashSet(StringComparer.Ordinal);
        var replacements = importedFingerprints.Count(currentFingerprints.Contains);
        var resultingDistinctItems = currentFingerprints.Count + importedFingerprints.Count - replacements;
        var removedByCapacity = Math.Max(0, resultingDistinctItems - Settings.MaxHistoryItems);
        return new HistoryImportPreview(
            items.Length,
            importedFingerprints.Count,
            replacements,
            removedByCapacity,
            Settings.MaxHistoryItems);
    }

    public int ExportSnippets(string path)
    {
        ThrowIfHistoryAccessLocked();

        var dto = CreateSnippetExportDto(out var count);
        WriteExportFile(path, dto);
        return count;
    }

    public int ExportSnippetsEncrypted(string path, string passphrase)
    {
        ThrowIfHistoryAccessLocked();

        var dto = CreateSnippetExportDto(out var count);
        WriteEncryptedExportFile(path, SnippetExportKind, dto, passphrase);
        return count;
    }

    private SnippetExportDto CreateSnippetExportDto(out int count)
    {
        var items = Snippets.Snippets.ToArray();
        count = items.Length;
        return new SnippetExportDto(1, DateTimeOffset.UtcNow, items);
    }

    public int ImportSnippets(string path)
    {
        ThrowIfHistoryAccessLocked();

        return ImportSnippets(ReadExportFile<SnippetExportDto>(path));
    }

    public int ImportSnippetsEncrypted(string path, string passphrase)
    {
        ThrowIfHistoryAccessLocked();

        return ImportSnippets(ReadEncryptedExportFile<SnippetExportDto>(path, SnippetExportKind, passphrase));
    }

    private int ImportSnippets(SnippetExportDto dto)
    {
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

    public SnippetImportPreview PreviewImportSnippets(string path)
    {
        ThrowIfHistoryAccessLocked();

        return PreviewImportSnippets(ReadExportFile<SnippetExportDto>(path));
    }

    public SnippetImportPreview PreviewImportSnippetsEncrypted(string path, string passphrase)
    {
        ThrowIfHistoryAccessLocked();

        return PreviewImportSnippets(ReadEncryptedExportFile<SnippetExportDto>(path, SnippetExportKind, passphrase));
    }

    private SnippetImportPreview PreviewImportSnippets(SnippetExportDto dto)
    {
        var items = dto.Items ?? throw new InvalidOperationException("The selected file does not contain snippet items.");
        var validItems = items
            .Where(snippet => !string.IsNullOrWhiteSpace(snippet.Name))
            .ToArray();
        var replacementKeys = validItems
            .Where(snippet => Snippets.Find(snippet.Folder, snippet.Name) is not null)
            .Select(snippet => $"{snippet.Folder.Trim()}\u001F{snippet.Name.Trim()}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        return new SnippetImportPreview(items.Length, validItems.Length, replacementKeys);
    }

    public void ClearHistory()
    {
        History.Clear();
        _deletedHistoryIds.Clear();
        _persistedHistoryCount = 0;
        _loadedPersistedHistoryCount = 0;
        Settings.PinnedHistoryIds = [];
        SaveSettings();
        SaveHistory();
        ClearHistoryImageFiles();
        _mainWindow?.RefreshItems();
    }

    public void ClearSnippets()
    {
        Snippets.Clear();
        SaveSnippets(_snippetPath, Snippets);
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
        InvalidateCachedQuickMenus();
        RefreshTrayText();
    }

    public void SetTheme(string theme)
    {
        Settings.Theme = NormalizeTheme(theme);
        SaveSettings();
        InvalidateCachedQuickMenus();
        RefreshTrayIcon();
        RefreshTrayText();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        IsExiting = true;
        if (_accessibilitySettings is not null)
        {
            try
            {
                _accessibilitySettings.HighContrastChanged -= OnHighContrastChanged;
            }
            catch (COMException exception)
            {
                AppDiagnostics.Log(exception, "High contrast event unsubscription");
            }
        }

        StopHistoryAccessLockTimer();
        _clipboardCaptureDelay?.Cancel();
        _clipboardCaptureDelay?.Dispose();
        _captureWorker.Dispose();
        SaveHistory(waitForCompletion: true);
        _messageWindow?.Dispose();
        _notifyIcon?.Dispose();
        InvalidateCachedQuickMenus();
        TryCloseWindow(_lifetimeWindow, "lifetime window");
    }

    public void ExitApplication()
    {
        try
        {
            Dispose();
        }
        finally
        {
            Application.Current.Exit();
        }
    }

    private void InvalidateCachedQuickMenus()
    {
        var defaultQuickMenu = _defaultQuickMenu;
        var richQuickMenu = _richQuickMenu;
        _defaultQuickMenu = null;
        _richQuickMenu = null;

        TryCloseWindow(defaultQuickMenu, "default quick menu");
        TryCloseWindow(richQuickMenu, "rich quick menu");
    }

    private void OnHighContrastChanged(AccessibilitySettings sender, object args)
    {
        NotifyHighContrastChanged();
    }

    internal void NotifyHighContrastChanged()
    {
        if (Volatile.Read(ref _disposeState) != 0)
        {
            return;
        }

        _ = _dispatcherQueue?.TryEnqueue(InvalidateCachedQuickMenus);
        HighContrastChanged?.Invoke(this, EventArgs.Empty);
    }

    private static void TryCloseWindow(Window? window, string description)
    {
        try
        {
            window?.Close();
        }
        catch (Exception exception) when (exception is COMException or InvalidOperationException)
        {
            AppDiagnostics.Log(exception, $"Close {description}");
        }
    }

    private void StartClipboardServices()
    {
        if (_clipboardServicesStarted)
        {
            return;
        }

        _messageWindow = new HotkeyMessageWindow(ShowQuickMenuOnUiThread, CaptureClipboardOnUiThread);
        RegisterHotkey(allowFallback: true);
        AppProfiler.Mark("Hotkey registered.");
        ScheduleClipboardCapture(IntPtr.Zero);
        AppProfiler.Mark("Initial clipboard capture scheduled.");
        _clipboardServicesStarted = true;
        AppDiagnostics.Info("Runtime", "Clipboard services started.");
    }

    public async void ShowHistoryWindow()
    {
        if (!await EnsureHistoryAccessUnlockedAsync(keepUnlockWindowVisible: true))
        {
            return;
        }

        ShowMainWindow();
        if (_mainWindow is not null)
        {
            await _mainWindow.ShowHistoryPageAsync();
        }
    }

    public async void ShowNewSnippetEditorWindow()
    {
        if (!await EnsureHistoryAccessUnlockedAsync(keepUnlockWindowVisible: true))
        {
            return;
        }

        ShowMainWindow();
        if (_mainWindow is not null)
        {
            await _mainWindow.ShowNewSnippetEditorAsync();
        }
    }

    private async Task<bool> EnsureHistoryAccessUnlockedAsync(bool keepUnlockWindowVisible)
    {
        if (!IsHistoryAccessLockEnabled)
        {
            return true;
        }

        if (!RequiresHistoryAccessUnlock)
        {
            RefreshHistoryAccessUnlockWindow();
            return true;
        }

        var shouldHideAfterUnlock = !keepUnlockWindowVisible
            && (_mainWindow is null || _mainWindow.IsHiddenToTray);
        var mainWindow = EnsureMainWindow();
        mainWindow.RefreshTexts();
        mainWindow.RefreshItems();
        var unlocked = await mainWindow.RequestHistoryAccessUnlockAsync(showWindow: true);
        if (unlocked && shouldHideAfterUnlock)
        {
            mainWindow.HideSettingsWindowToTray();
        }

        return unlocked;
    }

    private void CaptureClipboardOnUiThread(IntPtr sourceWindow)
    {
        var delay = Settings.ClipboardCaptureDelayMilliseconds;
        if (delay <= 0)
        {
            AppDiagnostics.Info("Clipboard", "Clipboard update received; capture scheduled immediately.");
            ScheduleClipboardCapture(sourceWindow);
            return;
        }

        AppDiagnostics.Info("Clipboard", $"Clipboard update received; capture delayed by {delay} ms.");
        var captureDelay = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _clipboardCaptureDelay, captureDelay);
        previous?.Cancel();
        previous?.Dispose();
        var token = captureDelay.Token;
        _ = Task.Delay(delay, token).ContinueWith(task =>
        {
            if (task.IsCanceled)
            {
                return;
            }

            if (task.Exception is not null)
            {
                AppDiagnostics.Log(task.Exception, "Clipboard capture delay");
                return;
            }

            try
            {
                ScheduleClipboardCapture(sourceWindow);
            }
            catch (Exception exception)
            {
                AppDiagnostics.Log(exception, "Clipboard capture delay");
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

        _pasteTargetWindow = CapturePasteTarget(pasteTargetWindow);
        if (_dispatcherQueue is null)
        {
            Volatile.Write(ref _quickMenuRequestPending, 0);
            return;
        }

        if (!_dispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                Volatile.Write(ref _lastQuickMenuRequestTick, Environment.TickCount64);
                if (await EnsureHistoryAccessUnlockedAsync(keepUnlockWindowVisible: false))
                {
                    ShowQuickMenu();
                }
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

    private void ScheduleClipboardCapture(IntPtr sourceWindow)
    {
        _captureWorker.Post(() => CaptureClipboardCore(sourceWindow));
    }

    private void CaptureClipboardCore(IntPtr sourceWindow)
    {
        var result = CaptureClipboardSnapshotCore(sourceWindow);
        if (result is null)
        {
            return;
        }

        _dispatcherQueue?.TryEnqueue(() => CommitCapturedClipboard(result.Snapshot, result.Sequence));
    }

    private ClipboardCaptureResult? CaptureClipboardSnapshotCore(IntPtr sourceWindow = default)
    {
        if (Settings.PauseCapture)
        {
            return null;
        }

        var sequence = NativeMethods.GetClipboardSequenceNumber();
        if (sequence != 0 && sequence == Volatile.Read(ref _lastCapturedClipboardSequence))
        {
            AppDiagnostics.Info("Clipboard", $"Clipboard sequence {sequence} already captured; skipping.");
            return null;
        }

        var origin = CaptureOriginMetadata(sourceWindow);
        if (ApplicationExclusionList.Matches(Settings.ExcludedCaptureApplicationPatterns, origin?.ApplicationName))
        {
            if (sequence != 0)
            {
                Volatile.Write(ref _lastCapturedClipboardSequence, sequence);
            }

            AppDiagnostics.Info("Clipboard", "Skipped clipboard capture because the source application is excluded.");
            return null;
        }

        var snapshot = ClipboardBridge.Capture();
        if (snapshot is null)
        {
            return null;
        }

        if (Settings.SaveHistorySourceMetadata)
        {
            snapshot = AttachOrigin(snapshot, origin);
        }

        return new ClipboardCaptureResult(ApplySourceMetadataPolicy(snapshot), sequence);
    }

    private ClipboardSnapshot AttachOrigin(ClipboardSnapshot snapshot, ClipboardOriginMetadata? origin)
    {
        if (origin is null)
        {
            return snapshot;
        }

        return new ClipboardSnapshot(
            snapshot.Id,
            snapshot.CapturedAt,
            snapshot.Formats,
            snapshot.Text,
            snapshot.Rtf,
            snapshot.Html,
            snapshot.ImagePng,
            snapshot.FilePaths,
            origin.ApplicationName,
            origin.WindowTitle);
    }

    private ClipboardSnapshot ApplySourceMetadataPolicy(ClipboardSnapshot snapshot)
    {
        if (Settings.SaveHistorySourceMetadata
            || (string.IsNullOrWhiteSpace(snapshot.SourceApplicationName)
                && string.IsNullOrWhiteSpace(snapshot.SourceWindowTitle)))
        {
            return snapshot;
        }

        return new ClipboardSnapshot(
            snapshot.Id,
            snapshot.CapturedAt,
            snapshot.Formats,
            snapshot.Text,
            snapshot.Rtf,
            snapshot.Html,
            snapshot.ImagePng,
            snapshot.FilePaths);
    }

    private void StripResidentHistorySourceMetadata()
    {
        if (History.Items.All(item => string.IsNullOrWhiteSpace(item.SourceApplicationName)
            && string.IsNullOrWhiteSpace(item.SourceWindowTitle)))
        {
            return;
        }

        var sanitized = History.Items.Select(ApplySourceMetadataPolicy).Reverse().ToArray();
        History.Clear();
        foreach (var item in sanitized)
        {
            History.Add(item);
        }

        SaveHistory();
    }

    private static ClipboardOriginMetadata? CaptureOriginMetadata(IntPtr sourceWindow)
    {
        if (sourceWindow == IntPtr.Zero)
        {
            return null;
        }

        var threadId = NativeMethods.GetWindowThreadProcessId(sourceWindow, out var processId);
        if (threadId == 0 || processId == 0 || processId == Environment.ProcessId)
        {
            return null;
        }

        string? applicationName = null;
        try
        {
            applicationName = GetProcessName(processId);
        }
        catch
        {
        }

        var windowTitle = GetWindowTitle(sourceWindow);
        if (string.IsNullOrWhiteSpace(applicationName) && string.IsNullOrWhiteSpace(windowTitle))
        {
            return null;
        }

        return new ClipboardOriginMetadata(applicationName, windowTitle);
    }

    private static string? GetProcessName(uint processId)
    {
        var process = NativeMethods.OpenProcess(NativeMethods.ProcessQueryLimitedInformation, false, processId);
        if (process == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var capacity = 1024;
            var builder = new StringBuilder(capacity);
            if (!NativeMethods.QueryFullProcessImageName(process, 0, builder, ref capacity) || capacity <= 0)
            {
                return null;
            }

            var path = builder.ToString();
            return string.IsNullOrWhiteSpace(path)
                ? null
                : Path.GetFileNameWithoutExtension(path);
        }
        finally
        {
            NativeMethods.CloseHandle(process);
        }
    }

    private static string? GetWindowTitle(IntPtr hwnd)
    {
        var length = NativeMethods.GetWindowTextLength(hwnd);
        if (length <= 0)
        {
            return null;
        }

        var builder = new System.Text.StringBuilder(length + 1);
        return NativeMethods.GetWindowText(hwnd, builder, builder.Capacity) > 0
            ? builder.ToString()
            : null;
    }

    private bool CommitCapturedClipboard(ClipboardSnapshot snapshot, uint sequence)
    {
        var lastSequence = Volatile.Read(ref _lastCapturedClipboardSequence);
        if (sequence != 0 && lastSequence != 0 && sequence < lastSequence)
        {
            AppDiagnostics.Info("Clipboard", $"Ignoring stale clipboard sequence {sequence}; latest is {lastSequence}.");
            return false;
        }

        Volatile.Write(ref _lastCapturedClipboardSequence, sequence);
        if (History.Add(snapshot))
        {
            AppDiagnostics.Info("Clipboard", $"Captured clipboard item with {snapshot.Formats.Count} format(s).");
            if (ShouldFlushResidentHistory())
            {
                SaveHistoryInBackground();
            }
            else
            {
                QueueHistorySave();
            }

            _mainWindow?.RefreshItemsIncremental();
            return true;
        }

        return false;
    }

    private void ScheduleClipboardRefreshForQuickMenu()
    {
        var startTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
        var task = _captureWorker.InvokeAsync<ClipboardCaptureResult>(() => CaptureClipboardSnapshotCore());
        _ = task.ContinueWith(completed =>
        {
            if (completed.Status == TaskStatus.RanToCompletion && completed.Result is { } result)
            {
                var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
                if (elapsed > QuickMenuClipboardCaptureTimeoutMilliseconds)
                {
                    AppDiagnostics.Info("Clipboard", $"Quick menu clipboard capture completed after {elapsed:F1}ms; refreshing current menu.");
                }

                _dispatcherQueue?.TryEnqueue(() =>
                {
                    if (CommitCapturedClipboard(result.Snapshot, result.Sequence))
                    {
                        RefreshOpenQuickMenuItems();
                    }
                });
            }
            else if (completed.Exception is not null)
            {
                AppDiagnostics.Log(completed.Exception, "Quick menu clipboard capture continuation");
            }
        }, TaskScheduler.Default);
    }

    private bool RegisterHotkey(bool allowFallback = false)
    {
        if (_messageWindow is null)
        {
            return false;
        }

        if (!HotkeyGesture.TryParse(Settings.Hotkey, out var preferredGesture))
        {
            preferredGesture = HotkeyGesture.Default;
        }

        if (_messageWindow.Register(preferredGesture))
        {
            SaveRegisteredHotkeyIfChanged(preferredGesture, allowFallback);
            return true;
        }

        if (!allowFallback)
        {
            return false;
        }

        foreach (var fallbackGesture in HotkeyGesture.GetRegistrationCandidates(preferredGesture).Skip(1))
        {
            if (!_messageWindow.Register(fallbackGesture))
            {
                continue;
            }

            AppDiagnostics.Info("Hotkey", $"Fell back to available hotkey {fallbackGesture}.");
            SaveRegisteredHotkeyIfChanged(fallbackGesture, save: true);
            return true;
        }

        AppDiagnostics.Info("Hotkey", "No configured hotkey preset could be registered.");
        return false;
    }

    private void SaveRegisteredHotkeyIfChanged(HotkeyGesture gesture, bool save)
    {
        var hotkey = gesture.ToString();
        if (string.Equals(Settings.Hotkey, hotkey, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Settings.Hotkey = hotkey;
        if (save)
        {
            SaveSettings();
        }
    }

    private void CreateTrayIcon()
    {
        _notifyIcon = new NativeTrayIcon(
            AppAssets.LoadTrayIcon(EffectiveTheme),
            Translate("History"),
            Translate("NewSnippet"),
            Translate("Settings"),
            Translate("Exit"),
            () => _dispatcherQueue?.TryEnqueue(async () =>
            {
                _pasteTargetWindow = default;
                if (await EnsureHistoryAccessUnlockedAsync(keepUnlockWindowVisible: false))
                {
                    ShowQuickMenu();
                }
            }),
            () => _dispatcherQueue?.TryEnqueue(ShowNewSnippetEditorWindow),
            () => _dispatcherQueue?.TryEnqueue(ShowMainWindow),
            () => _dispatcherQueue?.TryEnqueue(ExitApplication));
        RefreshTrayText();
    }

    private void CreateLifetimeWindow()
    {
        if (_lifetimeWindow is not null)
        {
            return;
        }

        _lifetimeWindow = new Window
        {
            Title = "Clipton"
        };
        _lifetimeWindow.Activate();

        var hwnd = WindowNative.GetWindowHandle(_lifetimeWindow);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.Resize(new SizeInt32(1, 1));
        appWindow.Hide();
    }

    private void RefreshTrayIcon()
    {
        if (_notifyIcon is null)
        {
            return;
        }

        _notifyIcon.UpdateIcon(AppAssets.LoadTrayIcon(EffectiveTheme));
    }

    private void RefreshTrayText()
    {
        if (_notifyIcon is null)
        {
            return;
        }

        _notifyIcon.UpdateMenuText(Translate("History"), Translate("NewSnippet"), Translate("Settings"), Translate("Exit"));
    }

    private void ShowQuickMenu()
    {
        var quickMenuStartTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
        AppProfiler.Mark("Quick menu requested.");

        ScheduleClipboardRefreshForQuickMenu();

        var menuItems = BuildQuickMenuItems();
        var quickMenuDisplayMode = NormalizeQuickMenuDisplayMode(Settings.QuickMenuDisplayMode);
        var useRichQuickMenu = string.Equals(quickMenuDisplayMode, "rich", StringComparison.OrdinalIgnoreCase);
        if (useRichQuickMenu)
        {
            ShowRichQuickMenu(menuItems, startInSearchMode: false);
        }
        else
        {
            ShowDefaultQuickMenu(menuItems);
        }

        AppProfiler.Mark($"Quick menu focused. mode={quickMenuDisplayMode}; total={(System.Diagnostics.Stopwatch.GetElapsedTime(quickMenuStartTimestamp).TotalMilliseconds):F1}ms; items={menuItems.Count}");
    }

    private sealed record ClipboardCaptureResult(ClipboardSnapshot Snapshot, uint Sequence);

    private sealed record ClipboardOriginMetadata(string? ApplicationName, string? WindowTitle);

    // The menu windows are cached and reused across invocations: tearing down
    // and recreating a WinUI window per hotkey press both leaks the hidden
    // window and makes the menu slower to appear.
    private void ShowDefaultQuickMenu(IReadOnlyList<QuickMenuItem> menuItems)
    {
        _richQuickMenu?.Dismiss();
        if (_defaultQuickMenu is null)
        {
            _defaultQuickMenu = new QuickMenuWindow(
                Translate("History"),
                menuItems,
                EffectiveTheme,
                Settings.QuickMenuImagePreviewSize,
                Settings.QuickMenuShowCapturedAt,
                Settings.QuickMenuShowShortcutHints,
                Settings.QuickMenuShortcuts,
                OpenQuickMenuSearch,
                Translate("PreviewImage"),
                Translate("QuickMenuPasteOptionsHelp"),
                new Dictionary<string, string>
                {
                    ["ImagePreviewOpenDefaultApp"] = Translate("ImagePreviewOpenDefaultApp"),
                    ["Paste"] = Translate("Paste"),
                    ["Copy"] = Translate("Copy"),
                    ["CopyAndRemove"] = Translate("CopyAndRemove"),
                    ["ZoomOut"] = Translate("ZoomOut"),
                    ["ResetZoom"] = Translate("ResetZoom"),
                    ["ZoomIn"] = Translate("ZoomIn"),
                    ["Close"] = Translate("Close"),
                    ["ImagePreviewFeedbackCopy"] = Translate("ImagePreviewFeedbackCopy"),
                    ["ImagePreviewFeedbackCut"] = Translate("ImagePreviewFeedbackCut"),
                    ["ImagePreviewFeedbackZoomIn"] = Translate("ImagePreviewFeedbackZoomIn"),
                    ["ImagePreviewFeedbackZoomOut"] = Translate("ImagePreviewFeedbackZoomOut"),
                    ["ImagePreviewFeedbackZoomReset"] = Translate("ImagePreviewFeedbackZoomReset"),
                    ["QuickMenuFolderLoading"] = Translate("QuickMenuFolderLoading"),
                    ["QuickMenuFolderNoItems"] = Translate("QuickMenuFolderNoItems")
                },
                GetEffectiveCulture());
            _defaultQuickMenu.FocusMenu();
            return;
        }

        _defaultQuickMenu.UpdateDisplayOptions(Settings.QuickMenuShowCapturedAt, Settings.QuickMenuShowShortcutHints);
        _defaultQuickMenu.Reopen(menuItems);
    }

    private void ShowRichQuickMenu(IReadOnlyList<QuickMenuItem> menuItems, bool startInSearchMode)
    {
        _defaultQuickMenu?.Dismiss();
        if (_richQuickMenu is null)
        {
            _richQuickMenu = CreateRichQuickMenuWindow(menuItems, startInSearchMode);
            _richQuickMenu.FocusMenu();
            return;
        }

        if (startInSearchMode)
        {
            _richQuickMenu.UpdateDisplayOptions(Settings.QuickMenuShowCapturedAt, Settings.QuickMenuShowShortcutHints);
            _richQuickMenu.ReopenWithSearch(menuItems);
        }
        else
        {
            _richQuickMenu.UpdateDisplayOptions(Settings.QuickMenuShowCapturedAt, Settings.QuickMenuShowShortcutHints);
            _richQuickMenu.Reopen(menuItems);
        }
    }

    // Opens the rich window focused on its search box. Used by the default
    // quick menu's search shortcut so both display modes share one search UI.
    private void OpenQuickMenuSearch()
    {
        ShowRichQuickMenu(BuildQuickMenuItems(), startInSearchMode: true);
    }

    private void RefreshOpenQuickMenuItems()
    {
        ApplyQuickMenuDisplayOptions();
        if (_defaultQuickMenu is { IsDismissed: false })
        {
            _defaultQuickMenu.Reopen(BuildQuickMenuItems());
            return;
        }

        if (_richQuickMenu is { IsDismissed: false })
        {
            _richQuickMenu.RefreshItems(BuildQuickMenuItems());
        }
    }

    private RichQuickMenuWindow CreateRichQuickMenuWindow(IReadOnlyList<QuickMenuItem> menuItems, bool startInSearchMode)
    {
        return new RichQuickMenuWindow(
            Translate("History"),
            menuItems,
            EffectiveTheme,
            Settings.QuickMenuShortcuts,
            ShowHistoryWindow,
            Translate("ShowAllHistory"),
            Translate("PreviewImage"),
            Translate("PasteOptions"),
            Translate("QuickMenuPasteOptionsButtonName"),
            Translate("QuickMenuPasteOptionsHelp"),
            Translate("ImagePreviewFeedbackCopy"),
            Translate("ImagePreviewFeedbackCut"),
            Translate("SearchPlaceholder"),
            Translate("PinnedHistory"),
            Translate("FormatText"),
            Translate("Back"),
            Translate("Close"),
            Translate("Paste"),
            Translate("PlainText"),
            Translate("QuickMenuFolderLoading"),
            Translate("QuickMenuFolderLoadingNamed"),
            Translate("RelativeTimeJustNow"),
            Translate("RelativeTimeSecondsAgo"),
            Translate("RelativeTimeMinutesAgo"),
            Translate("RelativeTimeHoursAgo"),
            GetEffectiveCulture(),
            Settings.QuickMenuShowCapturedAt,
            startInSearchMode);
    }

    private CultureInfo GetEffectiveCulture()
    {
        return CultureInfo.GetCultureInfo(EffectiveLocale);
    }

    private string FormatItemCount(int count)
    {
        return string.Format(GetEffectiveCulture(), Translate("QuickMenuItemCount"), count);
    }

    private void ApplyQuickMenuDisplayOptions()
    {
        _defaultQuickMenu?.UpdateDisplayOptions(Settings.QuickMenuShowCapturedAt, Settings.QuickMenuShowShortcutHints);
        _richQuickMenu?.UpdateDisplayOptions(Settings.QuickMenuShowCapturedAt, Settings.QuickMenuShowShortcutHints);
    }

    private List<QuickMenuItem> BuildQuickMenuItems()
    {
        var menuItems = new List<QuickMenuItem>();
        var pinnedIds = Settings.PinnedHistoryIds.ToHashSet(StringComparer.Ordinal);
        var historyCount = CountUnpinnedHistoryItems(pinnedIds);
        var pinnedItems = Settings.PinnedHistoryIds
            .Select(History.Find)
            .OfType<ClipboardSnapshot>()
            .ToArray();
        var topLevelHistoryItems = QuickMenuHistoryBuckets.NormalizeTopLevelHistoryItems(Settings.QuickMenuTopLevelHistoryItems);
        var includeImageThumbnails = !string.Equals(Settings.QuickMenuImagePreviewSize, "none", StringComparison.OrdinalIgnoreCase);
        var directHistoryItems = EnumerateUnpinnedHistoryItems(pinnedIds).Take(topLevelHistoryItems);
        foreach (var item in directHistoryItems)
        {
            menuItems.Add(CreateHistoryMenuItem(item, includeImageThumbnails));
        }

        AppProfiler.Mark($"Quick menu direct history items created. items={menuItems.Count}; historyCount={historyCount}; loaded={History.Items.Count}; persisted={_persistedHistoryCount}");
        AddHistoryRangeFolders(menuItems, historyCount, topLevelHistoryItems, pinnedIds, includeImageThumbnails);
        AppProfiler.Mark($"Quick menu history folders created. items={menuItems.Count}");

        if (pinnedItems.Length > 0)
        {
            menuItems.Add(new QuickMenuItem(
                Translate("PinnedHistory"),
                FormatItemCount(pinnedItems.Length),
                ">",
                "Enter",
                () => { },
                LazyChildren: () => pinnedItems.Select(item => CreateHistoryMenuItem(item, includeImageThumbnails)).ToArray(),
                IconGlyph: "",
                IconFontFamily: "Segoe Fluent Icons"));
        }

        var snippetItems = CreateSnippetMenuItems(Snippets.Snippets);
        AppProfiler.Mark($"Quick menu snippet items created. snippetItems={snippetItems.Count}");
        if (menuItems.Count > 0 && snippetItems.Count > 0)
        {
            menuItems.Add(QuickMenuItem.Separator());
        }

        menuItems.AddRange(snippetItems);

        if (menuItems.Count > 0)
        {
            menuItems.Add(QuickMenuItem.Separator());
            menuItems.Add(CreateNewSnippetQuickMenuItem());
            menuItems.Add(new QuickMenuItem(
                Translate("Settings"),
                "Clipton",
                "*",
                "Enter",
                ShowMainWindow,
                IconGlyph: "",
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
                IconGlyph: "",
                IconFontFamily: "Segoe Fluent Icons"));
        }

        if (menuItems.Count == 1 && string.Equals(menuItems[0].Title, Translate("HistoryEmpty"), StringComparison.Ordinal))
        {
            menuItems.Add(QuickMenuItem.Separator());
            menuItems.Add(CreateNewSnippetQuickMenuItem());
        }

        return menuItems;
    }

    private QuickMenuItem CreateNewSnippetQuickMenuItem()
    {
        return new QuickMenuItem(
            Translate("NewSnippet"),
            Translate("Snippets"),
            "+",
            "Enter",
            ShowNewSnippetEditorWindow,
            IconGlyph: "\uE710",
            IconFontFamily: "Segoe Fluent Icons");
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
        return LocalizationCatalog.NormalizeLocale(locale);
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

    private static string NormalizeQuickMenuDisplayMode(string? mode)
    {
        return string.Equals(mode, "rich", StringComparison.OrdinalIgnoreCase) ? "rich" : "default";
    }

    private static int NormalizeClipboardCaptureDelay(int milliseconds)
    {
        return milliseconds is 0 or 50 or 100 or 150 or 250 or 500 or 1000
            ? milliseconds
            : 150;
    }

    private int CountUnpinnedHistoryItems(ISet<string> pinnedIds)
    {
        var count = 0;
        foreach (var item in History.Items)
        {
            if (!pinnedIds.Contains(item.Id))
            {
                count++;
            }
        }

        var unloadedPersistedItems = Math.Max(0, _persistedHistoryCount - _loadedPersistedHistoryCount);
        return count + unloadedPersistedItems;
    }

    private IEnumerable<ClipboardSnapshot> EnumerateUnpinnedHistoryItems(ISet<string> pinnedIds)
    {
        foreach (var item in History.Items)
        {
            if (!pinnedIds.Contains(item.Id))
            {
                yield return item;
            }
        }
    }

    private void EnsurePersistedHistoryLoaded(int loadedPersistedCount)
    {
        if (!Settings.PersistEncryptedHistory || loadedPersistedCount <= _loadedPersistedHistoryCount)
        {
            return;
        }

        var target = Math.Min(Math.Min(loadedPersistedCount, Settings.MaxHistoryItems), _persistedHistoryCount);
        while (_loadedPersistedHistoryCount < target)
        {
            // Store reads (decryption, file I/O) stay on the calling thread;
            // only the History mutation is marshaled to the UI thread.
            var loadCount = Math.Min(QuickMenuHistoryBuckets.BucketSize, target - _loadedPersistedHistoryCount);
            var snapshots = _historyStore.LoadRange(_loadedPersistedHistoryCount, loadCount);
            if (!RunOnDispatcher(() =>
            {
                if (snapshots.Count == 0)
                {
                    _loadedPersistedHistoryCount = target;
                    return;
                }

                foreach (var snapshot in snapshots)
                {
                    History.AppendOlder(ApplySourceMetadataPolicy(snapshot));
                }

                _loadedPersistedHistoryCount += snapshots.Count;
            }))
            {
                break;
            }

            if (snapshots.Count == 0)
            {
                break;
            }
        }
    }

    private IReadOnlyList<ClipboardSnapshot> GetUnpinnedHistoryRange(ISet<string> pinnedIds, int offset, int count)
    {
        var targetLoadedPersistedCount = Math.Min(_persistedHistoryCount, offset + count + pinnedIds.Count);
        EnsurePersistedHistoryLoaded(targetLoadedPersistedCount);
        IReadOnlyList<ClipboardSnapshot> range = [];
        if (!RunOnDispatcher(() =>
        {
            range = EnumerateUnpinnedHistoryItems(pinnedIds)
                .Skip(offset)
                .Take(count)
                .ToArray();
        }))
        {
            return [];
        }

        return range;
    }

    // History and the loaded-count fields are UI-thread state, but folder
    // materialization and quick menu search call into them from background
    // threads. Mutating or enumerating the live list there races against the
    // UI thread (e.g. a clipboard capture committing at the same time).
    private bool RunOnDispatcher(Action action)
    {
        if (_dispatcherQueue is null)
        {
            action();
            return true;
        }

        if (_dispatcherQueue.HasThreadAccess)
        {
            action();
            return true;
        }

        var completed = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Exception? failure = null;
        var timedOut = 0;
        if (!_dispatcherQueue.TryEnqueue(() =>
        {
            if (Volatile.Read(ref timedOut) == 1)
            {
                completed.TrySetResult(null);
                return;
            }

            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                completed.TrySetResult(null);
            }
        }))
        {
            return false;
        }

        if (!completed.Task.Wait(DispatcherMarshalTimeout))
        {
            Volatile.Write(ref timedOut, 1);
            AppDiagnostics.Info("Dispatcher", $"Dispatcher marshal exceeded {DispatcherMarshalTimeout.TotalSeconds:F0}s; skipped result.");
            return false;
        }

        if (failure is not null)
        {
            AppDiagnostics.Log(failure, "Dispatcher marshal");
            return false;
        }

        return true;
    }

    private void AddHistoryRangeFolders(
        List<QuickMenuItem> menuItems,
        int historyCount,
        int topLevelCount,
        ISet<string> pinnedIds,
        bool includeImageThumbnails)
    {
        foreach (var range in QuickMenuHistoryBuckets.CreateTopLevelRanges(historyCount, topLevelCount))
        {
            if (range.IsNestedParent)
            {
                menuItems.Add(new QuickMenuItem(
                    range.Label,
                    FormatItemCount(range.Count),
                    ">",
                    "Enter",
                    () => { },
                    LazyChildren: () => CreateNestedHistoryRangeFolders(historyCount, pinnedIds, includeImageThumbnails)));
                continue;
            }

            AddTopLevelHistoryRangeFolder(menuItems, range, pinnedIds, includeImageThumbnails);
        }
    }

    private void AddTopLevelHistoryRangeFolder(
        List<QuickMenuItem> menuItems,
        QuickMenuHistoryRange range,
        ISet<string> pinnedIds,
        bool includeImageThumbnails)
    {
        menuItems.Add(new QuickMenuItem(
            range.Label,
            FormatItemCount(range.Count),
            ">",
            "Enter",
            () => { },
            LazyChildren: () => GetUnpinnedHistoryRange(pinnedIds, range.Offset, range.Count)
                .Select(item => CreateHistoryMenuItem(item, includeImageThumbnails))
                .ToArray()));
    }

    private IReadOnlyList<QuickMenuItem> CreateNestedHistoryRangeFolders(
        int historyCount,
        ISet<string> pinnedIds,
        bool includeImageThumbnails)
    {
        var folders = new List<QuickMenuItem>();
        foreach (var range in QuickMenuHistoryBuckets.CreateNestedRanges(historyCount))
        {
            folders.Add(new QuickMenuItem(
                range.Label,
                FormatItemCount(range.Count),
                ">",
                "Enter",
                () => { },
                LazyChildren: () => GetUnpinnedHistoryRange(pinnedIds, range.Offset, range.Count)
                    .Select(item => CreateHistoryMenuItem(item, includeImageThumbnails))
                    .ToArray()));
        }

        return folders;
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
        return LocalizationCatalog.ResolveLocale(locale);
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
        try
        {
            return FormatPackageVersion(Windows.ApplicationModel.Package.Current.Id.Version);
        }
        catch (InvalidOperationException)
        {
        }

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
            return $"{id.Name} {FormatPackageVersion(id.Version)}";
        }
        catch (InvalidOperationException)
        {
            return "Unpackaged";
        }
    }

    private static string FormatPackageVersion(Windows.ApplicationModel.PackageVersion version)
    {
        return $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
    }

    private bool SaveHistory(bool waitForCompletion = false)
    {
        Interlocked.Increment(ref _historySaveVersion);
        CancelPendingHistorySaveDebounce();
        var plan = CreateHistoryPersistPlan();
        var task = EnqueueHistoryPersist(plan);
        try
        {
            var completed = true;
            if (waitForCompletion)
            {
                task.Wait();
            }
            else
            {
                completed = task.Wait(TimeSpan.FromSeconds(10));
            }
            if (!completed)
            {
                AppDiagnostics.Info("History", "History save did not complete before timeout; state counters will update after persistence completes.");
                ContinueFinishHistoryPersist(task, plan);
                return false;
            }

            if (task.Result)
            {
                FinishHistoryPersist(plan);
                return true;
            }
        }
        catch (AggregateException exception)
        {
            AppDiagnostics.Log(exception, "History save");
        }

        return false;
    }

    // Background saves are chained through _historyPersistChain so slow disk or DPAPI work
    // cannot reorder older snapshots after newer snapshots.
    private void SaveHistoryInBackground()
    {
        Interlocked.Increment(ref _historySaveVersion);
        CancelPendingHistorySaveDebounce();
        var plan = CreateHistoryPersistPlan();
        ContinueFinishHistoryPersist(EnqueueHistoryPersist(plan), plan);
    }

    private void ContinueFinishHistoryPersist(Task<bool> task, HistoryPersistPlan plan)
    {
        _ = task.ContinueWith(
            completed =>
            {
                if (completed.Status != TaskStatus.RanToCompletion || !completed.Result)
                {
                    if (completed.Exception is not null)
                    {
                        AppDiagnostics.Log(completed.Exception, "History save continuation");
                    }

                    return;
                }

                if (_dispatcherQueue is { } dispatcherQueue)
                {
                    dispatcherQueue.TryEnqueue(() => FinishHistoryPersist(plan));
                }
                else
                {
                    FinishHistoryPersist(plan);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default);
    }

    private void CancelPendingHistorySaveDebounce()
    {
        lock (_historySaveGate)
        {
            _historySaveDebounce?.Cancel();
            _historySaveDebounce = null;
        }
    }

    private HistoryPersistPlan CreateHistoryPersistPlan()
    {
        // Capture the save inputs on the UI thread before persistence moves to a worker.
        // The resident history list can change during clipboard captures and UI commands.
        return new HistoryPersistPlan(
            Settings.PersistEncryptedHistory,
            History.Items.ToArray(),
            _loadedPersistedHistoryCount,
            UnloadedPersistedHistoryCount,
            Settings.MaxHistoryItems);
    }

    private Task<bool> EnqueueHistoryPersist(HistoryPersistPlan plan)
    {
        lock (_historyPersistChainGate)
        {
            // ContinueWith intentionally serializes all persistence work behind the last
            // scheduled write, regardless of whether callers requested sync or background save.
            var task = _historyPersistChain.ContinueWith(
                _ => PersistHistory(plan),
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default);
            _historyPersistChain = task;
            return task;
        }
    }

    private bool PersistHistory(HistoryPersistPlan plan)
    {
        try
        {
            if (!plan.Persist)
            {
                _historyStore.Delete();
                return true;
            }

            // If older items are still only on disk, merge by id order in the store instead
            // of saving only the resident prefix.
            if (plan.UnloadedCount > 0)
            {
                _historyStore.SavePreservingOlder(plan.Snapshots, plan.LoadedPersistedCount, plan.Capacity);
            }
            else
            {
                _historyStore.Save(plan.Snapshots);
            }

            PruneHistoryImageFiles(plan.Snapshots.Select(item => item.Id).ToHashSet(StringComparer.Ordinal));
            return true;
        }
        catch (Exception exception)
        {
            AppDiagnostics.Log(exception, "History persist");
            return false;
        }
    }

    private void FinishHistoryPersist(HistoryPersistPlan plan)
    {
        if (!plan.Persist)
        {
            return;
        }

        _persistedHistoryCount = Math.Min(plan.Capacity, Math.Max(_persistedHistoryCount, plan.Snapshots.Length + plan.UnloadedCount));
        TrimLoadedHistoryAfterPersist();
    }

    private sealed record HistoryPersistPlan(
        bool Persist,
        ClipboardSnapshot[] Snapshots,
        int LoadedPersistedCount,
        int UnloadedCount,
        int Capacity);

    private void QueueHistorySave()
    {
        if (!Settings.PersistEncryptedHistory)
        {
            return;
        }

        // Coalesce clipboard-change bursts. The version check prevents an older debounce
        // task from saving after a newer immediate/background save has been requested.
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
                    if (_dispatcherQueue is { } dispatcherQueue)
                    {
                        dispatcherQueue.TryEnqueue(SaveHistoryInBackground);
                    }
                    else
                    {
                        SaveHistoryInBackground();
                    }
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

    private void TrimLoadedHistoryAfterPersist()
    {
        if (!Settings.PersistEncryptedHistory || History.Items.Count <= InitialPersistedHistoryLoadCount)
        {
            return;
        }

        History.UnloadOlderBeyond(InitialPersistedHistoryLoadCount);
        _persistedHistoryCount = Math.Max(_persistedHistoryCount, _historyStore.Count());
        _loadedPersistedHistoryCount = Math.Min(History.Items.Count, _persistedHistoryCount);
        PruneHistoryImageFiles(History.Items.Select(item => item.Id).ToHashSet(StringComparer.Ordinal));
        _mainWindow?.RefreshItems();
    }

    private bool ShouldFlushResidentHistory()
    {
        if (!Settings.PersistEncryptedHistory
            || History.Items.Count <= InitialPersistedHistoryLoadCount + ResidentHistoryFlushOverflowCount)
        {
            return false;
        }

        var now = Environment.TickCount64;
        var last = Volatile.Read(ref _lastImmediateHistorySaveTick);
        if (now - last < ImmediateHistorySaveMinIntervalMilliseconds)
        {
            return false;
        }

        Interlocked.Exchange(ref _lastImmediateHistorySaveTick, now);
        return true;
    }

    private void EnsureDefaultSnippets()
    {
        // An existing empty file means snippets were intentionally cleared.
        if (Snippets.Snippets.Count == 0 && File.Exists(_snippetPath))
        {
            return;
        }

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

    private static SnippetCatalog LoadSnippets(string protectedPath, string legacyPath, out bool loadedLegacy)
    {
        loadedLegacy = false;
        var catalog = new SnippetCatalog();
        if (File.Exists(protectedPath))
        {
            try
            {
                var encrypted = File.ReadAllBytes(protectedPath);
                var json = ProtectedData.Unprotect(encrypted, optionalEntropy: null, DataProtectionScope.CurrentUser);
                var snippets = JsonSerializer.Deserialize<Snippet[]>(json) ?? [];
                foreach (var snippet in snippets)
                {
                    catalog.Upsert(snippet);
                }
            }
            catch (Exception exception) when (exception is CryptographicException or JsonException or IOException or UnauthorizedAccessException)
            {
                AppDiagnostics.Log(exception, "Load protected snippets");
            }

            return catalog;
        }

        if (!File.Exists(legacyPath))
        {
            return catalog;
        }

        try
        {
            using var stream = File.OpenRead(legacyPath);
            var legacySnippets = JsonSerializer.Deserialize<Snippet[]>(stream) ?? [];
            foreach (var snippet in legacySnippets)
            {
                catalog.Upsert(snippet);
            }

            loadedLegacy = true;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            AppDiagnostics.Log(exception, "Load legacy snippets");
        }

        return catalog;
    }

    private static void SaveSnippets(string path, SnippetCatalog catalog)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.SerializeToUtf8Bytes(catalog.Snippets, new JsonSerializerOptions { WriteIndented = true });
        var encrypted = ProtectedData.Protect(json, optionalEntropy: null, DataProtectionScope.CurrentUser);
        var tempPath = Path.Combine(Path.GetDirectoryName(path)!, $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        File.WriteAllBytes(tempPath, encrypted);
        File.Move(tempPath, path, overwrite: true);
    }

    private static T ReadExportFile<T>(string path)
    {
        EncryptedExportFile.EnsureFileWithinImportLimit(path);
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<T>(stream, ExportJsonOptions)
            ?? throw new InvalidOperationException("The selected file is not a supported Clipton export.");
    }

    private static void WriteExportFile<T>(string path, T value)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        WriteJsonFile(path, value, ExportJsonOptions);
    }

    private static T ReadEncryptedExportFile<T>(string path, string kind, string passphrase)
    {
        return EncryptedExportFile.Read<T>(path, kind, passphrase, ExportJsonOptions);
    }

    private static void WriteEncryptedExportFile<T>(string path, string kind, T value, string passphrase)
    {
        EncryptedExportFile.Write(path, kind, value, passphrase, ExportJsonOptions);
    }

    private static void WriteJsonFile<T>(string path, T value, JsonSerializerOptions options)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(value, options));
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
        string[] FilePaths,
        string? SourceApplicationName = null,
        string? SourceWindowTitle = null)
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
                snapshot.FilePaths.ToArray(),
                snapshot.SourceApplicationName,
                snapshot.SourceWindowTitle);
        }

        public ClipboardSnapshot ToSnapshot()
        {
            return new ClipboardSnapshot(
                // Export ids are untrusted file content and must never become storage
                // filenames. Imports always receive a fresh app-owned identifier.
                Guid.NewGuid().ToString("N"),
                CapturedAt == default ? DateTimeOffset.UtcNow : CapturedAt,
                Formats is { Length: > 0 } ? Formats : InferFormats(),
                Text,
                Rtf,
                Html,
                ImagePng,
                FilePaths,
                SourceApplicationName,
                SourceWindowTitle);
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

    private static PasteTargetWindowIdentity CapturePasteTarget(IntPtr window)
    {
        if (window == IntPtr.Zero || !NativeMethods.IsWindow(window))
        {
            return default;
        }

        var threadId = NativeMethods.GetWindowThreadProcessId(window, out var processId);
        return threadId == 0 || processId == 0
            ? default
            : new PasteTargetWindowIdentity(window, threadId, processId);
    }

    private void SendPaste(PasteTargetWindowIdentity pasteTargetWindow)
    {
        if (!RestorePasteTarget(pasteTargetWindow)
            || !WaitForPasteTargetForeground(pasteTargetWindow))
        {
            AppDiagnostics.Warning("Paste", "Paste was canceled because the original target window could not be restored.");
            return;
        }

        if (!WaitForModifierKeyRelease())
        {
            AppDiagnostics.Warning("Paste", "Paste was canceled because a modifier key remained pressed.");
            return;
        }

        // Revalidate immediately before injecting keys. The HWND may have been destroyed
        // and reused, or focus may have moved while clipboard work was in progress.
        if (!IsPasteTargetForeground(pasteTargetWindow))
        {
            AppDiagnostics.Warning("Paste", "Paste was canceled because focus moved away from the original target window.");
            return;
        }

        NativeMethods.keybd_event(NativeMethods.VkControl, 0, 0, UIntPtr.Zero);
        NativeMethods.keybd_event(NativeMethods.VkV, 0, 0, UIntPtr.Zero);
        Thread.Sleep(20);
        NativeMethods.keybd_event(NativeMethods.VkV, 0, NativeMethods.KeyeventfKeyup, UIntPtr.Zero);
        NativeMethods.keybd_event(NativeMethods.VkControl, 0, NativeMethods.KeyeventfKeyup, UIntPtr.Zero);
    }

    // Shift/Alt/Win still held from the hotkey or Enter press would turn the
    // injected Ctrl+V into a different chord (e.g. Ctrl+Shift+V) in the target app.
    private static bool WaitForModifierKeyRelease()
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
                return true;
            }

            Thread.Sleep(15);
        }

        return false;
    }

    private static bool RestorePasteTarget(PasteTargetWindowIdentity pasteTargetWindow)
    {
        if (!IsPasteTargetCurrent(pasteTargetWindow))
        {
            return false;
        }

        var currentThread = NativeMethods.GetCurrentThreadId();
        var targetThread = pasteTargetWindow.ThreadId;
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
            NativeMethods.BringWindowToTop(pasteTargetWindow.Handle);
            NativeMethods.SetForegroundWindow(pasteTargetWindow.Handle);
            NativeMethods.SetActiveWindow(pasteTargetWindow.Handle);
            NativeMethods.SetFocus(pasteTargetWindow.Handle);
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

        return true;
    }

    private static bool WaitForPasteTargetForeground(PasteTargetWindowIdentity pasteTargetWindow)
    {
        var deadline = Environment.TickCount64 + 180;
        while (Environment.TickCount64 < deadline)
        {
            if (IsPasteTargetForeground(pasteTargetWindow))
            {
                return true;
            }

            Thread.Sleep(10);
        }

        return false;
    }

    private static bool IsPasteTargetCurrent(PasteTargetWindowIdentity pasteTargetWindow)
    {
        if (pasteTargetWindow.IsEmpty || !NativeMethods.IsWindow(pasteTargetWindow.Handle))
        {
            return false;
        }

        var threadId = NativeMethods.GetWindowThreadProcessId(pasteTargetWindow.Handle, out var processId);
        return pasteTargetWindow.Matches(pasteTargetWindow.Handle, threadId, processId);
    }

    private static bool IsPasteTargetForeground(PasteTargetWindowIdentity pasteTargetWindow)
    {
        if (!IsPasteTargetCurrent(pasteTargetWindow))
        {
            return false;
        }

        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground != pasteTargetWindow.Handle)
        {
            return false;
        }

        var foregroundThread = NativeMethods.GetWindowThreadProcessId(foreground, out var foregroundProcess);
        return pasteTargetWindow.Matches(foreground, foregroundThread, foregroundProcess);
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

    private QuickMenuItem CreateHistoryMenuItem(ClipboardSnapshot item, bool includeImageThumbnail)
    {
        var display = CreateHistoryItemViewModel(item, includeThumbnail: includeImageThumbnail);
        var header = display.Preview;
        var revealedHeader = IsMaskedHistoryItem(item)
            ? item.Preview
            : null;
        var plainText = ClipboardBridge.GetPlainText(item);
        var textActionTitle = string.IsNullOrWhiteSpace(display.Preview) ? Translate("QuickEdit") : display.Preview;
        Func<IReadOnlyList<QuickMenuPasteOption>> pasteOptionsFactory = item.ImagePng is { Length: > 0 }
            ? () => CreateImagePasteOptions(item)
            : item.FilePaths.Count > 0
                ? () => CreateFilePasteOptions(item)
                : () => CreateTextPasteOptions(plainText, item.Id);
        return new QuickMenuItem(
            header,
            display.FormatSummary,
            GetKindLabel(item),
            BuildHistoryCommandHint(plainText),
            () => PasteHistoryItem(item.Id, asPlainText: false),
            !string.IsNullOrEmpty(plainText) ? () => PasteHistoryItem(item.Id, asPlainText: true) : null,
            LazyPasteOptions: pasteOptionsFactory,
            IconGlyph: GetHistoryIconGlyph(item),
            IconFontFamily: GetHistoryIconFontFamily(item),
            IconImageBytes: includeImageThumbnail ? GetHistoryThumbnailBytes(item) : null,
            PreviewImageBytesProvider: item.ImagePng is { Length: > 0 } ? () => GetHistoryImagePreviewBytes(item) : null,
            CopyInvoke: item.ImagePng is { Length: > 0 } ? () => PasteImage(item.Id, ImagePasteMode.Png, sendPaste: false) : null,
            CutInvoke: item.ImagePng is { Length: > 0 } ? () => CutImageHistoryItem(item.Id) : null,
            EditInvoke: !string.IsNullOrEmpty(plainText) ? () => QuickEditAndPasteText(plainText, textActionTitle) : null,
            PreviewInvoke: !string.IsNullOrEmpty(plainText) ? () => PreviewText(plainText, textActionTitle) : null,
            RevealedTitle: revealedHeader,
            CapturedAt: item.CapturedAt,
            IsPinned: IsHistoryPinned(item.Id),
            Formats: item.Formats,
            IsNumberShortcutEnabled: true);
    }

    private void CutImageHistoryItem(string id)
    {
        PasteImage(id, ImagePasteMode.Png, sendPaste: false);
        RemoveHistoryItem(id);
    }

    private string BuildHistoryCommandHint(string? plainText)
    {
        if (string.IsNullOrEmpty(plainText))
        {
            return "Enter";
        }

        var plainTextShortcut = Settings.QuickMenuShortcuts?.PastePlainText;
        var hint = "Enter / E";
        return string.IsNullOrWhiteSpace(plainTextShortcut)
            ? hint
            : $"{hint} / {plainTextShortcut}";
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

    private byte[]? GetHistoryThumbnailBytes(ClipboardSnapshot item)
    {
        if (item.ImagePng is not { Length: > 0 })
        {
            return null;
        }

        try
        {
            Directory.CreateDirectory(_thumbnailPath);
            var cacheKey = $"{item.Id}-96";
            if (TryGetCachedHistoryImageBytes(cacheKey) is { } cachedBytes)
            {
                return cachedBytes;
            }

            var path = Path.Combine(_thumbnailPath, $"{item.Id}-96.bin");
            if (File.Exists(path))
            {
                var bytes = ReadProtectedBytes(path);
                CacheHistoryImageBytes(cacheKey, bytes);
                return bytes;
            }

            var legacyPath = Path.Combine(_thumbnailPath, $"{item.Id}-96.png");
            if (File.Exists(legacyPath))
            {
                var legacyBytes = File.ReadAllBytes(legacyPath);
                WriteProtectedBytes(path, legacyBytes);
                TryDeleteFile(legacyPath);
                CacheHistoryImageBytes(cacheKey, legacyBytes);
                return legacyBytes;
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
            using var thumbnailStream = new MemoryStream();
            thumbnail.Save(thumbnailStream, Imaging.ImageFormat.Png);
            var thumbnailBytes = thumbnailStream.ToArray();
            WriteProtectedBytes(path, thumbnailBytes);
            CacheHistoryImageBytes(cacheKey, thumbnailBytes);
            return thumbnailBytes;
        }
        catch
        {
            return null;
        }
    }

    private byte[]? GetHistoryImagePreviewBytes(ClipboardSnapshot item)
    {
        if (item.ImagePng is not { Length: > 0 })
        {
            return null;
        }

        try
        {
            Directory.CreateDirectory(_thumbnailPath);
            var cacheKey = $"{item.Id}-preview";
            if (TryGetCachedHistoryImageBytes(cacheKey) is { } cachedBytes)
            {
                return cachedBytes;
            }

            var path = Path.Combine(_thumbnailPath, $"{item.Id}-preview.bin");
            if (File.Exists(path))
            {
                var bytes = ReadProtectedBytes(path);
                CacheHistoryImageBytes(cacheKey, bytes);
                return bytes;
            }

            var legacyPath = Path.Combine(_thumbnailPath, $"{item.Id}-preview.png");
            if (File.Exists(legacyPath))
            {
                var legacyBytes = File.ReadAllBytes(legacyPath);
                WriteProtectedBytes(path, legacyBytes);
                TryDeleteFile(legacyPath);
                CacheHistoryImageBytes(cacheKey, legacyBytes);
                return legacyBytes;
            }

            WriteProtectedBytes(path, item.ImagePng);
            CacheHistoryImageBytes(cacheKey, item.ImagePng);
            return item.ImagePng;
        }
        catch
        {
            return null;
        }
    }

    public byte[]? GetHistoryImagePreviewBytes(string id)
    {
        return History.Find(id) is { } item ? GetHistoryImagePreviewBytes(item) : null;
    }

    private void DeleteHistoryImageFiles(string id)
    {
        if (!Directory.Exists(_thumbnailPath))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(_thumbnailPath, $"{id}-*.*"))
        {
            TryDeleteFile(file);
        }

        RemoveCachedHistoryImageFiles(id);
    }

    private void ClearHistoryImageFiles()
    {
        if (!Directory.Exists(_thumbnailPath))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(_thumbnailPath, "*.*"))
        {
            TryDeleteFile(file);
        }

        lock (_historyImageCacheGate)
        {
            _historyImageBytesByKey.Clear();
        }
    }

    private void PruneHistoryImageFiles(HashSet<string> activeHistoryIds)
    {
        if (!Directory.Exists(_thumbnailPath))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(_thumbnailPath, "*.*"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var id = GetHistoryImageFileId(name);
            if (string.IsNullOrEmpty(id))
            {
                continue;
            }

            if (!activeHistoryIds.Contains(id))
            {
                TryDeleteFile(file);
                RemoveCachedHistoryImageFiles(id);
            }
        }
    }

    private byte[]? TryGetCachedHistoryImageBytes(string key)
    {
        lock (_historyImageCacheGate)
        {
            return _historyImageBytesByKey.GetValueOrDefault(key);
        }
    }

    private void CacheHistoryImageBytes(string key, byte[] bytes)
    {
        lock (_historyImageCacheGate)
        {
            if (!_historyImageBytesByKey.ContainsKey(key))
            {
                _historyImageCacheKeys.Enqueue(key);
            }

            _historyImageBytesByKey[key] = bytes;
            while (_historyImageBytesByKey.Count > MaxCachedHistoryImages && _historyImageCacheKeys.Count > 0)
            {
                var evictedKey = _historyImageCacheKeys.Dequeue();
                _historyImageBytesByKey.Remove(evictedKey);
            }
        }
    }

    private void RemoveCachedHistoryImageFiles(string id)
    {
        lock (_historyImageCacheGate)
        {
            _historyImageBytesByKey.Remove($"{id}-96");
            _historyImageBytesByKey.Remove($"{id}-preview");
        }
    }

    private static string? GetHistoryImageFileId(string fileNameWithoutExtension)
    {
        const string thumbnailSuffix = "-96";
        const string previewSuffix = "-preview";
        if (fileNameWithoutExtension.EndsWith(thumbnailSuffix, StringComparison.Ordinal))
        {
            return fileNameWithoutExtension[..^thumbnailSuffix.Length];
        }

        if (fileNameWithoutExtension.EndsWith(previewSuffix, StringComparison.Ordinal))
        {
            return fileNameWithoutExtension[..^previewSuffix.Length];
        }

        return null;
    }

    private static byte[] ReadProtectedBytes(string path)
    {
        var encrypted = File.ReadAllBytes(path);
        return ProtectedData.Unprotect(encrypted, optionalEntropy: null, DataProtectionScope.CurrentUser);
    }

    private static void WriteProtectedBytes(string path, byte[] bytes)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var encrypted = ProtectedData.Protect(bytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        var tempPath = Path.Combine(directory ?? string.Empty, $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        File.WriteAllBytes(tempPath, encrypted);
        File.Move(tempPath, path, overwrite: true);
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

    /// <summary>
    /// Builds the display model for one history item, applying masking and thumbnail policy.
    /// </summary>
    /// <remarks>
    /// Masking affects only the preview text and metadata label. The original snapshot
    /// remains available for paste operations.
    /// </remarks>
    public HistoryItemViewModel CreateHistoryItemViewModel(ClipboardSnapshot snapshot, bool includeThumbnail = true)
    {
        var formats = CreateFormatSummary(snapshot.Formats);
        var metadata = CreateHistoryMetadataSummary(snapshot, formats);
        var plainText = ClipboardBridge.GetPlainText(snapshot);
        var thumbnailBytes = includeThumbnail ? GetHistoryThumbnailBytes(snapshot) : null;
        var isImage = snapshot.Formats.Contains(ClipboardFormatKind.Image);
        var snippet = Snippets.FindByText(plainText);
        if (snippet is not null)
        {
            return new HistoryItemViewModel(snapshot.Id, snippet.DisplayName, $"{Translate("RegisteredSnippetMasked")} - {metadata}", snapshot.CapturedAt, FormatCapturedAt(snapshot.CapturedAt), isImage, thumbnailBytes);
        }

        if (Settings.MaskSensitiveContent && CreateMaskedPreview(plainText) is { } maskedPreview)
        {
            return new HistoryItemViewModel(snapshot.Id, NormalizePreviewText(maskedPreview), $"{Translate("MaskedSensitive")} - {metadata}", snapshot.CapturedAt, FormatCapturedAt(snapshot.CapturedAt), isImage, thumbnailBytes);
        }

        return new HistoryItemViewModel(snapshot.Id, CreatePreviewText(snapshot, plainText), metadata, snapshot.CapturedAt, FormatCapturedAt(snapshot.CapturedAt), isImage, thumbnailBytes);
    }

    private string FormatCapturedAt(DateTimeOffset capturedAt)
    {
        return capturedAt.LocalDateTime.ToString("g", GetEffectiveCulture());
    }

    private static string CreateHistoryMetadataSummary(ClipboardSnapshot snapshot, string formats)
    {
        var origin = CreateOriginSummary(snapshot);
        return string.IsNullOrWhiteSpace(origin) ? formats : $"{formats} · {origin}";
    }

    private static string? CreateOriginSummary(ClipboardSnapshot snapshot)
    {
        var applicationName = snapshot.SourceApplicationName;
        var windowTitle = snapshot.SourceWindowTitle;
        if (string.IsNullOrWhiteSpace(applicationName))
        {
            return string.IsNullOrWhiteSpace(windowTitle) ? null : windowTitle;
        }

        if (string.IsNullOrWhiteSpace(windowTitle)
            || windowTitle.Equals(applicationName, StringComparison.OrdinalIgnoreCase))
        {
            return applicationName;
        }

        return $"{applicationName} - {windowTitle}";
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
        var previewScanText = SensitiveContentDetector.CreatePreviewScanText(plainText);
        return SensitiveContentDetector.CreateMaskedPreview(
            previewScanText,
            Settings.MaskVisiblePrefixLength,
            Settings.MaskRuleDefinitions,
            Settings.CustomMaskPatterns,
            Settings.MaskRules.CustomPattern);
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
        var trimmed = text.ReplaceLineEndings("\n").Trim();
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        var preview = string.Join(
            PreviewLineBreakMarker,
            trimmed.Split('\n').Select(NormalizePreviewLine));
        return Regex.Replace(preview, " {2,}", " ").Trim();
    }

    private static string NormalizePreviewLine(string line)
    {
        return string.Join(" ", line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private IReadOnlyList<QuickMenuItem> CreateSnippetMenuItems(IEnumerable<Snippet> snippets)
    {
        var root = new SnippetMenuFolderNode(string.Empty);
        foreach (var snippet in snippets)
        {
            var node = root;
            foreach (var segment in NormalizeFolder(snippet.Folder).Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                node = node.GetOrAddChild(segment);
            }

            node.Snippets.Add(snippet);
        }

        return CreateSnippetMenuItems(root);
    }

    private IReadOnlyList<QuickMenuItem> CreateSnippetMenuItems(SnippetMenuFolderNode folder)
    {
        var items = folder.Snippets
            .OrderBy(snippet => snippet.Name, StringComparer.OrdinalIgnoreCase)
            .Select(CreateSnippetMenuItem)
            .ToList();

        foreach (var child in folder.Children.Values.OrderBy(child => child.Name, StringComparer.OrdinalIgnoreCase))
        {
            items.Add(new QuickMenuItem(
                child.Name,
                Translate("Snippets"),
                ">",
                "Enter",
                () => { },
                LazyChildren: () => CreateSnippetMenuItems(child)));
        }

        return items;
    }

    private QuickMenuItem CreateSnippetMenuItem(Snippet snippet)
    {
        var requiresFilePaths = SnippetTemplateRenderer.RequiresFilePaths(snippet.Text);
        string RenderSnippetText() => SnippetTemplateRenderer.Render(
            snippet.Text,
            filePaths: requiresFilePaths ? GetCurrentClipboardFilePaths() : null);

        return new QuickMenuItem(
            snippet.Name,
            string.IsNullOrWhiteSpace(snippet.Folder) ? Translate("Snippets") : snippet.Folder,
            "S",
            "Enter / E",
            () => PasteSnippet(snippet.Folder, snippet.Name),
            PlainTextInvoke: () => PasteSnippet(snippet.Folder, snippet.Name),
            LazyPasteOptions: () => CreateTextPasteOptions(RenderSnippetText),
            IconGlyph: "S",
            EditInvoke: () => QuickEditAndPasteText(RenderSnippetText(), snippet.Name),
            PreviewInvoke: () => PreviewText(RenderSnippetText(), snippet.Name),
            IsNumberShortcutEnabled: true);
    }

    private IReadOnlyList<string> GetCurrentClipboardFilePaths()
    {
        return ClipboardBridge.Capture()?.FilePaths ?? [];
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
                : EnabledPasteOptions([CreatePinPasteOption(historyId)]);
        }

        var options = new List<QuickMenuPasteOption>
        {
            new QuickMenuPasteOption(Translate("PastePlain"), "\uE8D2", () => PasteText(textFactory() ?? string.Empty), Id: QuickMenuPasteOptionIds.PastePlain),
            new QuickMenuPasteOption(Translate("EditAndPaste"), "\uE70F", () => QuickEditAndPasteText(textFactory() ?? string.Empty, Translate("QuickEdit")), Id: QuickMenuPasteOptionIds.EditAndPaste),
            new QuickMenuPasteOption(Translate("PasteNoLineBreaks"), "\uE8EE", () => PasteText(RemoveLineBreaks(textFactory() ?? string.Empty)), Id: QuickMenuPasteOptionIds.PasteNoLineBreaks),
            new QuickMenuPasteOption(Translate("PasteUppercase"), "AA", () => PasteText((textFactory() ?? string.Empty).ToUpperInvariant()), "Segoe UI", QuickMenuPasteOptionIds.PasteUppercase),
            new QuickMenuPasteOption(Translate("PasteLowercase"), "aa", () => PasteText((textFactory() ?? string.Empty).ToLowerInvariant()), "Segoe UI", QuickMenuPasteOptionIds.PasteLowercase),
            new QuickMenuPasteOption(Translate("PasteTrimmed"), "\uE8C6", () => PasteText((textFactory() ?? string.Empty).Trim()), Id: QuickMenuPasteOptionIds.PasteTrimmed),
            new QuickMenuPasteOption(Translate("PasteJsonString"), "{ }", () => PasteText(InferredJsonFormatter.Format(textFactory() ?? string.Empty)), "Segoe UI", QuickMenuPasteOptionIds.PasteJsonString)
        };

        var urls = ExtractUrls(text);
        if (urls.Length > 0)
        {
            options.Add(new QuickMenuPasteOption(Translate("PasteExtractUrls"), "\uE71B", () => PasteText(string.Join(Environment.NewLine, urls)), Id: QuickMenuPasteOptionIds.PasteExtractUrls));
        }

        if (TryFormatJson(text) is { } formattedJson)
        {
            options.Add(new QuickMenuPasteOption(Translate("PasteFormattedJson"), "{ }", () => PasteText(formattedJson), "Segoe UI", QuickMenuPasteOptionIds.PasteFormattedJson));
        }

        if (historyId is not null)
        {
            options.Add(CreatePinPasteOption(historyId));
        }

        return EnabledPasteOptions(options);
    }

    private QuickMenuPasteOption CreatePinPasteOption(string historyId)
    {
        return IsHistoryPinned(historyId)
            ? new QuickMenuPasteOption(Translate("UnpinHistory"), "\uE77A", () => TogglePinnedHistoryItem(historyId), Id: QuickMenuPasteOptionIds.TogglePin)
            : new QuickMenuPasteOption(Translate("PinHistory"), "\uE718", () => TogglePinnedHistoryItem(historyId), Id: QuickMenuPasteOptionIds.TogglePin);
    }

    private IReadOnlyList<QuickMenuPasteOption> EnabledPasteOptions(IEnumerable<QuickMenuPasteOption> options)
    {
        return options
            .Where(option => string.IsNullOrWhiteSpace(option.Id) || IsQuickMenuPasteOptionEnabled(option.Id))
            .ToArray();
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

    private IReadOnlyList<QuickMenuPasteOption> CreateFilePasteOptions(ClipboardSnapshot item)
    {
        var options = new List<QuickMenuPasteOption>
        {
            new(Translate("PasteOriginal"), "\uE8A5", () => PasteHistoryItem(item.Id, asPlainText: false), Id: QuickMenuPasteOptionIds.PasteOriginal),
            new(Translate("PasteFilePaths"), "\uE8C8", () => PasteText(JoinFileValues(item.FilePaths, path => path)), Id: QuickMenuPasteOptionIds.PasteFilePaths),
            new(Translate("PasteFileNames"), "\uE8A7", () => PasteText(JoinFileValues(item.FilePaths, Path.GetFileName)), Id: QuickMenuPasteOptionIds.PasteFileNames),
            new(Translate("PasteFileNamesWithoutExtension"), "\uE8A7", () => PasteText(JoinFileValues(item.FilePaths, Path.GetFileNameWithoutExtension)), Id: QuickMenuPasteOptionIds.PasteFileNamesWithoutExtension),
            new(Translate("PasteFileDirectories"), "\uED43", () => PasteText(JoinFileValues(item.FilePaths, path => Path.GetDirectoryName(path) ?? string.Empty)), Id: QuickMenuPasteOptionIds.PasteFileDirectories)
        };

        options.Add(CreatePinPasteOption(item.Id));
        return EnabledPasteOptions(options);
    }

    private static string JoinFileValues(IEnumerable<string> filePaths, Func<string, string?> selector)
    {
        return string.Join(Environment.NewLine, filePaths.Select(selector).Where(value => !string.IsNullOrEmpty(value)));
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

        var plainText = ClipboardBridge.GetPlainText(item);
        var originalLabel = string.IsNullOrEmpty(plainText)
            ? Translate("PasteImageOriginal")
            : Translate("PasteOriginal");
        var options = new List<QuickMenuPasteOption>
        {
            new QuickMenuPasteOption(originalLabel, "\uEB9F", () => PasteImage(item.Id, ImagePasteMode.Original, sendPaste: true), Id: QuickMenuPasteOptionIds.PasteImageOriginal),
            new QuickMenuPasteOption(Translate("PasteImagePng"), "PNG", () => PasteImage(item.Id, ImagePasteMode.Png, sendPaste: true), "Segoe UI", QuickMenuPasteOptionIds.PasteImagePng),
            new QuickMenuPasteOption(Translate("PasteImageJpeg"), "JPG", () => PasteImage(item.Id, ImagePasteMode.Jpeg, sendPaste: true), "Segoe UI", QuickMenuPasteOptionIds.PasteImageJpeg),
            new QuickMenuPasteOption(Translate("PasteImageResizeHalf"), "50%", () => PasteImage(item.Id, ImagePasteMode.ResizedHalf, sendPaste: true), "Segoe UI", QuickMenuPasteOptionIds.PasteImageResizeHalf),
            new QuickMenuPasteOption(Translate("PasteImageFile"), "\uE8A5", () => PasteImage(item.Id, ImagePasteMode.File, sendPaste: true), Id: QuickMenuPasteOptionIds.PasteImageFile),
            new QuickMenuPasteOption(Translate("CopyImageOnly"), "\uE8C8", () => PasteImage(item.Id, ImagePasteMode.Png, sendPaste: false), Id: QuickMenuPasteOptionIds.CopyImageOnly)
        };

        options.AddRange(CreateTextPasteOptions(plainText));
        options.Add(CreatePinPasteOption(item.Id));
        return EnabledPasteOptions(options);
    }

    private string CreateTempImageFile(ClipboardSnapshot item)
    {
        CleanupTempPasteFiles();
        EnsurePrivateTempPasteDirectory();
        var path = Path.Combine(_tempPastePath, $"paste-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(path, item.ImagePng!);
        RestrictPathToCurrentUser(path, isDirectory: false);
        return path;
    }

    private void EnsurePrivateTempPasteDirectory()
    {
        Directory.CreateDirectory(_tempPastePath);
        RestrictPathToCurrentUser(_tempPastePath, isDirectory: true);
    }

    private static void RestrictPathToCurrentUser(string path, bool isDirectory)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            var currentUser = WindowsIdentity.GetCurrent().User;
            if (currentUser is null)
            {
                return;
            }

            if (isDirectory)
            {
                var security = new DirectorySecurity();
                security.SetOwner(currentUser);
                security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
                security.AddAccessRule(new FileSystemAccessRule(
                    currentUser,
                    FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AccessControlType.Allow));
                new DirectoryInfo(path).SetAccessControl(security);
                return;
            }

            var fileSecurity = new FileSecurity();
            fileSecurity.SetOwner(currentUser);
            fileSecurity.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            fileSecurity.AddAccessRule(new FileSystemAccessRule(
                currentUser,
                FileSystemRights.FullControl,
                AccessControlType.Allow));
            new FileInfo(path).SetAccessControl(fileSecurity);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or PlatformNotSupportedException
            or ArgumentException
            or InvalidOperationException
            or System.Security.SecurityException)
        {
            AppDiagnostics.Log(exception, "Restrict temporary paste file ACL");
        }
    }

    private static byte[] ResizeImagePng(byte[] imagePng, double scale)
    {
        using var input = new MemoryStream(imagePng);
        using var source = Drawing.Image.FromStream(input);
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        using var resized = new Drawing.Bitmap(width, height, Imaging.PixelFormat.Format32bppPArgb);
        if (source.HorizontalResolution > 0 && source.VerticalResolution > 0)
        {
            resized.SetResolution(source.HorizontalResolution, source.VerticalResolution);
        }

        using (var graphics = Drawing.Graphics.FromImage(resized))
        {
            graphics.Clear(Drawing.Color.Transparent);
            graphics.CompositingQuality = Drawing2D.CompositingQuality.HighQuality;
            graphics.InterpolationMode = Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = Drawing2D.PixelOffsetMode.HighQuality;
            graphics.SmoothingMode = Drawing2D.SmoothingMode.HighQuality;
            graphics.DrawImage(source, 0, 0, width, height);
        }

        using var output = new MemoryStream();
        resized.Save(output, Imaging.ImageFormat.Png);
        return output.ToArray();
    }

    private void CleanupTempPasteFiles(bool deleteAll = false)
    {
        try
        {
            var directory = new DirectoryInfo(_tempPastePath);
            if (!directory.Exists)
            {
                return;
            }

            var cutoff = DateTime.UtcNow - TempPasteMaxAge;
            var currentFiles = new List<FileInfo>();
            foreach (var file in directory.EnumerateFiles("paste-*.png"))
            {
                if (deleteAll || file.CreationTimeUtc < cutoff)
                {
                    TryDeleteFile(file);
                }
                else
                {
                    currentFiles.Add(file);
                }
            }

            if (currentFiles.Count <= TempPasteMaxFiles)
            {
                return;
            }

            foreach (var file in currentFiles
                .OrderByDescending(file => file.CreationTimeUtc)
                .Skip(TempPasteMaxFiles))
            {
                TryDeleteFile(file);
            }
        }
        catch
        {
            // Temp files are best-effort cleanup; paste behavior must not depend on cleanup success.
        }
    }

    private void ScheduleTempPasteFileDeletion(string path) => ScheduleTempPasteFileDeletion(path, TempPasteDeleteDelay);

    private void ScheduleTempPasteFileDeletion(string path, TimeSpan delay)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay);
                TryDeleteFile(path);
            }
            catch
            {
            }
        });
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

    private sealed class SnippetMenuFolderNode(string name)
    {
        public string Name { get; } = name;

        public List<Snippet> Snippets { get; } = [];

        public Dictionary<string, SnippetMenuFolderNode> Children { get; } = new(StringComparer.OrdinalIgnoreCase);

        public SnippetMenuFolderNode GetOrAddChild(string childName)
        {
            if (!Children.TryGetValue(childName, out var child))
            {
                child = new SnippetMenuFolderNode(childName);
                Children[childName] = child;
            }

            return child;
        }
    }
}

internal readonly record struct PasteTargetWindowIdentity(IntPtr Handle, uint ThreadId, uint ProcessId)
{
    internal bool IsEmpty => Handle == IntPtr.Zero || ThreadId == 0 || ProcessId == 0;

    internal bool Matches(IntPtr handle, uint threadId, uint processId)
    {
        return !IsEmpty
            && Handle == handle
            && ThreadId == threadId
            && ProcessId == processId;
    }
}

internal enum MaskedHistoryKind
{
    None,
    Sensitive,
    RegisteredSnippet
}

/// <summary>
/// Image paste transformations exposed by the quick menu.
/// </summary>
public enum ImagePasteMode
{
    /// <summary>Paste the original captured image payload.</summary>
    Original,

    /// <summary>Paste image bytes as PNG bitmap data.</summary>
    Png,

    /// <summary>Paste image bytes converted to JPEG bitmap data.</summary>
    Jpeg,

    /// <summary>Paste image bytes resized to 50% as PNG bitmap data.</summary>
    ResizedHalf,

    /// <summary>Paste a temporary PNG file path as a file drop.</summary>
    File
}

/// <summary>
/// Summary of a history import before the user confirms it.
/// </summary>
public sealed record HistoryImportPreview(
    int SourceItems,
    int UniqueItems,
    int ReplacementItems,
    int RemovedByCapacityItems,
    int Capacity);

/// <summary>
/// Summary of a snippet import before the user confirms it.
/// </summary>
public sealed record SnippetImportPreview(
    int SourceItems,
    int ValidItems,
    int ReplacementItems);
