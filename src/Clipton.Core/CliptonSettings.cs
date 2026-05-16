namespace Clipton.Core;

public sealed class CliptonSettings
{
    public string Hotkey { get; set; } = HotkeyGesture.Default.ToString();

    public bool StartWithWindows { get; set; }

    public bool PastePlainTextByDefault { get; set; }

    public int MaxHistoryItems { get; set; } = 30;

    public string Locale { get; set; } = "en";
}
