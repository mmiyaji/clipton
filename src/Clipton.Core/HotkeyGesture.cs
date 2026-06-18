namespace Clipton.Core;

/// <summary>
/// Modifier flags accepted by Clipton's global hotkey parser.
/// </summary>
[Flags]
public enum HotkeyModifiers
{
    /// <summary>No modifier keys.</summary>
    None = 0,

    /// <summary>The Alt modifier.</summary>
    Alt = 1,

    /// <summary>The Control modifier.</summary>
    Control = 2,

    /// <summary>The Shift modifier.</summary>
    Shift = 4,

    /// <summary>The Windows logo key modifier.</summary>
    Windows = 8
}

/// <summary>
/// User-configurable global hotkey gesture.
/// </summary>
/// <remarks>
/// The parser accepts a deliberately small gesture surface. Global hotkeys are shared
/// with Windows and every running app, so unsupported keys and Shift-only shortcuts are
/// rejected instead of being normalized into potentially invasive registrations.
/// </remarks>
public sealed record HotkeyGesture(HotkeyModifiers Modifiers, string Key)
{
    /// <summary>Default gesture used on first launch and when parsing fails.</summary>
    public static HotkeyGesture Default { get; } = new(HotkeyModifiers.Control | HotkeyModifiers.Alt, "V");

    /// <summary>Fallback registrations attempted when the preferred gesture is unavailable.</summary>
    public static IReadOnlyList<HotkeyGesture> Presets { get; } =
    [
        Default,
        new(HotkeyModifiers.Control | HotkeyModifiers.Alt, "SPACE"),
        new(HotkeyModifiers.Control | HotkeyModifiers.Shift, "V")
    ];

    /// <summary>
    /// Returns the preferred gesture followed by safe presets, without duplicates.
    /// </summary>
    public static IEnumerable<HotkeyGesture> GetRegistrationCandidates(HotkeyGesture preferred)
    {
        yield return preferred;
        foreach (var preset in Presets)
        {
            if (preset != preferred)
            {
                yield return preset;
            }
        }
    }

    /// <summary>Formats the gesture in the same text form accepted by <see cref="TryParse"/>.</summary>
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

        parts.Add(FormatKey(Key));
        return string.Join("+", parts);
    }

    /// <summary>
    /// Parses a user or settings value into a supported global hotkey gesture.
    /// </summary>
    /// <returns><see langword="true"/> when the value is syntactically valid and safe to register.</returns>
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

        if (key is null || !IsSafeModifierCombination(modifiers) || !IsSupportedKey(key))
        {
            return false;
        }

        gesture = new HotkeyGesture(modifiers, key);
        return true;
    }

    private static bool IsSafeModifierCombination(HotkeyModifiers modifiers)
    {
        return modifiers.HasFlag(HotkeyModifiers.Control)
            || modifiers.HasFlag(HotkeyModifiers.Alt)
            || modifiers.HasFlag(HotkeyModifiers.Windows);
    }

    private static bool IsSupportedKey(string key)
    {
        if (key.Length == 1)
        {
            var c = char.ToUpperInvariant(key[0]);
            return c is >= 'A' and <= 'Z' or >= '0' and <= '9';
        }

        if (string.Equals(key, "SPACE", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return key.StartsWith('F')
            && int.TryParse(key[1..], out var functionKey)
            && functionKey is >= 1 and <= 24;
    }

    private static string FormatKey(string key)
    {
        return string.Equals(key, "SPACE", StringComparison.OrdinalIgnoreCase)
            ? "Space"
            : key.ToUpperInvariant();
    }
}
