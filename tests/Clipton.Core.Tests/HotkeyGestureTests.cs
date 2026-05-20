using Clipton.Core;

namespace Clipton.Core.Tests;

public sealed class HotkeyGestureTests
{
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
