using Clipton.Core;

namespace Clipton.Core.Tests;

public sealed class HotkeyGestureTests
{
    [Fact]
    public void Default_IsCtrlAltV()
    {
        Assert.Equal("Ctrl+Alt+V", HotkeyGesture.Default.ToString());
    }

    [Fact]
    public void Presets_StartWithDefaultAndIncludeFallbacks()
    {
        Assert.Equal(
            ["Ctrl+Alt+V", "Ctrl+Alt+Space", "Ctrl+Shift+V"],
            HotkeyGesture.Presets.Select(preset => preset.ToString()).ToArray());
    }

    [Fact]
    public void GetRegistrationCandidates_PutsPreferredFirstThenPresetFallbacks()
    {
        Assert.Equal(
            ["Ctrl+Shift+V", "Ctrl+Alt+V", "Ctrl+Alt+Space"],
            HotkeyGesture.GetRegistrationCandidates(new HotkeyGesture(HotkeyModifiers.Control | HotkeyModifiers.Shift, "V"))
                .Select(candidate => candidate.ToString())
                .ToArray());
    }

    [Theory]
    [InlineData("Ctrl+Shift+V", HotkeyModifiers.Control | HotkeyModifiers.Shift, "V")]
    [InlineData("Control + Alt + Space", HotkeyModifiers.Control | HotkeyModifiers.Alt, "SPACE")]
    [InlineData("Win+V", HotkeyModifiers.Windows, "V")]
    public void TryParse_ParsesSupportedGestures(string value, HotkeyModifiers modifiers, string key)
    {
        Assert.True(HotkeyGesture.TryParse(value, out var gesture));
        Assert.Equal(modifiers, gesture.Modifiers);
        Assert.Equal(key, gesture.Key);
    }

    [Theory]
    [InlineData("")]
    [InlineData("V")]
    [InlineData("Ctrl")]
    [InlineData("Shift+V")]
    [InlineData("Ctrl+Escape")]
    public void TryParse_RejectsInvalidGestures(string value)
    {
        Assert.False(HotkeyGesture.TryParse(value, out _));
    }
}
