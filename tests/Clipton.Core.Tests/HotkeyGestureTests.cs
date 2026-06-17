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
    [InlineData("Alt+F24", HotkeyModifiers.Alt, "F24")]
    [InlineData("Windows+0", HotkeyModifiers.Windows, "0")]
    public void TryParse_ParsesSupportedGestures(string value, HotkeyModifiers modifiers, string key)
    {
        Assert.True(HotkeyGesture.TryParse(value, out var gesture));
        Assert.Equal(modifiers, gesture.Modifiers);
        Assert.Equal(key, gesture.Key);
    }

    [Fact]
    public void ToString_IncludesAllModifiersInStableOrder()
    {
        var gesture = new HotkeyGesture(
            HotkeyModifiers.Control | HotkeyModifiers.Shift | HotkeyModifiers.Alt | HotkeyModifiers.Windows,
            "space");

        Assert.Equal("Ctrl+Shift+Alt+Win+Space", gesture.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("V")]
    [InlineData("Ctrl")]
    [InlineData("Shift+V")]
    [InlineData("Ctrl+Escape")]
    [InlineData("Ctrl+?")]
    [InlineData("Ctrl+F0")]
    [InlineData("Ctrl+F25")]
    [InlineData("Ctrl+Fx")]
    public void TryParse_RejectsInvalidGestures(string value)
    {
        Assert.False(HotkeyGesture.TryParse(value, out _));
    }
}
