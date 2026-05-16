namespace Clipton.Core;

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Windows = 8
}

public sealed record HotkeyGesture(HotkeyModifiers Modifiers, string Key)
{
    public static HotkeyGesture Default { get; } = new(HotkeyModifiers.Control | HotkeyModifiers.Shift, "V");

    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(HotkeyModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Windows))
        {
            parts.Add("Win");
        }

        parts.Add(Key.ToUpperInvariant());
        return string.Join("+", parts);
    }

    public static bool TryParse(string value, out HotkeyGesture gesture)
    {
        gesture = Default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var modifiers = HotkeyModifiers.None;
        string? key = null;

        foreach (var part in value.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.ToUpperInvariant())
            {
                case "CTRL":
                case "CONTROL":
                    modifiers |= HotkeyModifiers.Control;
                    break;
                case "SHIFT":
                    modifiers |= HotkeyModifiers.Shift;
                    break;
                case "ALT":
                    modifiers |= HotkeyModifiers.Alt;
                    break;
                case "WIN":
                case "WINDOWS":
                    modifiers |= HotkeyModifiers.Windows;
                    break;
                default:
                    key = part.ToUpperInvariant();
                    break;
            }
        }

        if (key is null || modifiers == HotkeyModifiers.None)
        {
            return false;
        }

        gesture = new HotkeyGesture(modifiers, key);
        return true;
    }
}
