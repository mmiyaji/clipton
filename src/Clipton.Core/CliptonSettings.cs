namespace Clipton.Core;

public sealed class CliptonSettings
{
    public string Hotkey { get; set; } = HotkeyGesture.Default.ToString();

    public bool StartWithWindows { get; set; }

    public bool HideSettingsWindowOnStartup { get; set; } = true;

    public bool InitialLaunchCompleted { get; set; }

    public bool PastePlainTextByDefault { get; set; }

    public bool PauseCapture { get; set; }

    public bool DiagnosticLoggingEnabled { get; set; }

    public bool PersistEncryptedHistory { get; set; } = true;

    public bool HistoryPersistenceConfigured { get; set; }

    public int ClipboardCaptureDelayMilliseconds { get; set; } = 150;

    public bool FolderMode { get; set; } = true;

    public bool SimpleContextMenuMode { get; set; }

    public string QuickMenuImagePreviewSize { get; set; } = "medium";

    public bool QuickMenuShowCapturedAt { get; set; }

    public bool QuickMenuShowShortcutHints { get; set; } = true;

    public QuickMenuShortcutSettings QuickMenuShortcuts { get; set; } = new();

    public bool MaskSensitiveContent { get; set; } = true;

    public int MaskVisiblePrefixLength { get; set; } = 3;

    public string[] CustomMaskPatterns { get; set; } = [];

    public int MaxHistoryItems { get; set; } = 200;

    public string[] PinnedHistoryIds { get; set; } = [];

    public string Theme { get; set; } = "system";

    public string Locale { get; set; } = "system";
}

public sealed class QuickMenuShortcutSettings
{
    public const string DefaultSearch = "Ctrl+F";
    public const string DefaultPastePlainText = "T";
    public const string DefaultToggleMaskReveal = "M";
    public const string DefaultToggleCapturedAt = "Ctrl+D";

    public string Search { get; set; } = DefaultSearch;

    public string PastePlainText { get; set; } = DefaultPastePlainText;

    public string ToggleMaskReveal { get; set; } = DefaultToggleMaskReveal;

    public string ToggleCapturedAt { get; set; } = DefaultToggleCapturedAt;
}
