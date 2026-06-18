# C# Documentation Comments

Clipton uses C# XML documentation comments for public contracts and short `//`
comments for local implementation decisions.

## When to use `///`

Use XML documentation comments on public or test-facing types and members when a
caller needs to know one of these things:

- Ownership or lifetime of state.
- Compatibility constraints for persisted data or settings.
- Side effects such as filesystem, clipboard, registry, or Windows API calls.
- Non-obvious return values, especially `null` or `false` as a safe fallback.
- Deliberate limits, normalization, or lossy transformations.

Prefer `<summary>` for the direct contract and `<remarks>` for design intent.
Use `<param>`, `<returns>`, and `<exception>` only when they add information that
is not obvious from the signature.

```csharp
/// <summary>
/// Loads a newest-first range from the persisted history.
/// </summary>
/// <param name="offset">Zero-based item offset.</param>
/// <returns>Snapshots that could be read and decrypted.</returns>
public IReadOnlyList<ClipboardSnapshot> LoadRange(int offset, int count)
```

## When to use `//`

Use ordinary comments inside a method for a local decision that future edits
could accidentally break:

```csharp
// The manifest is the ordering source of truth; item files not referenced by it
// are stale encrypted payloads and should be removed after each complete save.
```

## What to avoid

- Do not repeat the code in prose.
- Do not document every property by default on DTO-like settings objects.
- Do not describe intended behavior that the code does not actually implement.
- Do not let comments become a substitute for tests around compatibility or data loss.
