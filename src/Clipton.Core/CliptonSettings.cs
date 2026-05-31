namespace Clipton.Core;

public sealed class CliptonSettings
{
    public string Hotkey { get; set; } = HotkeyGesture.Default.ToString();

    public bool StartWithWindows { get; set; }

    public bool HideSettingsWindowOnStartup { get; set; } = true;

    public bool InitialLaunchCompleted { get; set; }

    public bool PastePlainTextByDefault { get; set; }

    public bool PauseCapture { get; set; }

    public bool PersistEncryptedHistory { get; set; } = true;

    public bool HistoryPersistenceConfigured { get; set; }

    public bool FolderMode { get; set; }

    public bool SimpleContextMenuMode { get; set; }

    public string QuickMenuImagePreviewSize { get; set; } = "medium";

    public bool MaskSensitiveContent { get; set; } = true;

    public int MaskVisiblePrefixLength { get; set; } = 3;

    public string[] CustomMaskPatterns { get; set; } = [];

    public int MaxHistoryItems { get; set; } = 200;

    public string[] PinnedHistoryIds { get; set; } = [];

    public string Theme { get; set; } = "light";

    public string Locale { get; set; } = "en";
}
