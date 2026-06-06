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

    public int QuickMenuTopLevelHistoryItems { get; set; } = 5;

    public bool SimpleContextMenuMode { get; set; }

    public string QuickMenuImagePreviewSize { get; set; } = "medium";

    public bool QuickMenuShowCapturedAt { get; set; }

    public bool QuickMenuShowShortcutHints { get; set; } = true;

    public QuickMenuShortcutSettings QuickMenuShortcuts { get; set; } = new();

    public bool MaskSensitiveContent { get; set; } = true;

    public int MaskVisiblePrefixLength { get; set; } = 3;

    public MaskRuleSettings MaskRules { get; set; } = new();

    public MaskRuleDefinition[] MaskRuleDefinitions { get; set; } = MaskRuleDefinitionDefaults.CreateDefaultRules();

    public string[] CustomMaskPatterns { get; set; } = [];

    public int MaxHistoryItems { get; set; } = 200;

    public string[] PinnedHistoryIds { get; set; } = [];

    public string Theme { get; set; } = "system";

    public string Locale { get; set; } = "system";
}

public sealed class MaskRuleSettings
{
    public bool Email { get; set; } = true;

    public bool CreditCard { get; set; } = true;

    public bool SecretKeyword { get; set; } = true;

    public bool BearerToken { get; set; } = true;

    public bool LongToken { get; set; } = true;

    public bool ShortAlphanumericCode { get; set; } = true;

    public bool PhoneNumber { get; set; } = true;

    public bool CustomPattern { get; set; } = true;
}

public sealed class MaskRuleDefinition
{
    public string Id { get; set; } = string.Empty;

    public string NameKey { get; set; } = string.Empty;

    public string Pattern { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public bool BuiltIn { get; set; } = true;

    public int Order { get; set; }
}

public static class MaskRuleIds
{
    public const string Email = "email";
    public const string CreditCard = "credit-card";
    public const string SecretKeyword = "secret-keyword";
    public const string BearerToken = "bearer-token";
    public const string LongToken = "long-token";
    public const string ShortAlphanumericCode = "short-alphanumeric-code";
    public const string PhoneNumber = "phone-number";
}

public static class MaskRuleDefinitionDefaults
{
    public static MaskRuleDefinition[] CreateDefaultRules(MaskRuleSettings? settings = null)
    {
        settings ??= new MaskRuleSettings();
        return
        [
            BuiltIn(MaskRuleIds.Email, "MaskRuleEmail", 10, settings.Email,
                @"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b"),
            BuiltIn(MaskRuleIds.CreditCard, "MaskRuleCreditCard", 20, settings.CreditCard,
                @"\b(?:\d[ -]*?){13,19}\b"),
            BuiltIn(MaskRuleIds.SecretKeyword, "MaskRuleSecretKeyword", 30, settings.SecretKeyword,
                @"\b(password|passwd|pwd|secret|token|api[_-]?key|access[_-]?key|private[_-]?key)\b(\s*[:=]\s*)([^\s,;]+)"),
            BuiltIn(MaskRuleIds.BearerToken, "MaskRuleBearerToken", 40, settings.BearerToken,
                @"\bbearer(\s+)([A-Za-z0-9._\-]{8,})\b"),
            BuiltIn(MaskRuleIds.LongToken, "MaskRuleLongToken", 50, settings.LongToken,
                @"\b[A-Za-z0-9_\-]{32,}\b"),
            BuiltIn(MaskRuleIds.ShortAlphanumericCode, "MaskRuleShortAlphanumericCode", 60, settings.ShortAlphanumericCode,
                @"\b(?=[A-Za-z0-9]*[A-Za-z])(?=[A-Za-z0-9]*\d)[A-Za-z0-9]{8,}\b"),
            BuiltIn(MaskRuleIds.PhoneNumber, "MaskRulePhoneNumber", 70, settings.PhoneNumber,
                @"(?<!\d)(?:\+?\d{1,3}[-. ]?)?(?:\(?\d{2,4}\)?[-. ]?){2,4}\d{3,4}(?!\d)")
        ];
    }

    public static MaskRuleDefinition[] Normalize(IEnumerable<MaskRuleDefinition>? definitions, MaskRuleSettings? fallbackSettings = null)
    {
        var defaults = CreateDefaultRules(fallbackSettings);
        var byId = (definitions ?? [])
            .Where(rule => !string.IsNullOrWhiteSpace(rule.Id))
            .GroupBy(rule => rule.Id.Trim(), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        return defaults.Select(defaultRule =>
        {
            if (!byId.TryGetValue(defaultRule.Id, out var configured))
            {
                return defaultRule;
            }

            return new MaskRuleDefinition
            {
                Id = defaultRule.Id,
                NameKey = string.IsNullOrWhiteSpace(configured.NameKey) ? defaultRule.NameKey : configured.NameKey.Trim(),
                Pattern = string.IsNullOrWhiteSpace(configured.Pattern) ? defaultRule.Pattern : configured.Pattern.Trim(),
                Enabled = configured.Enabled,
                BuiltIn = true,
                Order = configured.Order > 0 ? configured.Order : defaultRule.Order
            };
        })
        .OrderBy(rule => rule.Order)
        .ToArray();
    }

    public static MaskRuleSettings ToSettings(IEnumerable<MaskRuleDefinition>? definitions)
    {
        var byId = Normalize(definitions).ToDictionary(rule => rule.Id, StringComparer.Ordinal);
        return new MaskRuleSettings
        {
            Email = byId[MaskRuleIds.Email].Enabled,
            CreditCard = byId[MaskRuleIds.CreditCard].Enabled,
            SecretKeyword = byId[MaskRuleIds.SecretKeyword].Enabled,
            BearerToken = byId[MaskRuleIds.BearerToken].Enabled,
            LongToken = byId[MaskRuleIds.LongToken].Enabled,
            ShortAlphanumericCode = byId[MaskRuleIds.ShortAlphanumericCode].Enabled,
            PhoneNumber = byId[MaskRuleIds.PhoneNumber].Enabled,
            CustomPattern = true
        };
    }

    private static MaskRuleDefinition BuiltIn(string id, string nameKey, int order, bool enabled, string pattern)
    {
        return new MaskRuleDefinition
        {
            Id = id,
            NameKey = nameKey,
            Pattern = pattern,
            Enabled = enabled,
            BuiltIn = true,
            Order = order
        };
    }
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
