using System.Text.Json;
using Clipton.Core;
using Clipton.WinUI;

namespace Clipton.WinUI.Tests;

public sealed class HistoryAccessLockPrivacyTests
{
    [Fact]
    public void ConfigureHistoryAccessLock_PersistsHashWithoutPlainPin()
    {
        var root = CreateTestRoot();
        using var runtime = new CliptonRuntime(root, isSafeMode: true);
        const string pin = "987654321098";

        runtime.ConfigureHistoryAccessLock(pin, timeoutMinutes: 15);

        var settingsJson = File.ReadAllText(Path.Combine(root, "settings.json"));
        using var document = JsonDocument.Parse(settingsJson);
        var settings = document.RootElement;

        Assert.True(settings.GetProperty(nameof(CliptonSettings.HistoryAccessLockEnabled)).GetBoolean());
        Assert.NotEqual(pin, settings.GetProperty(nameof(CliptonSettings.HistoryAccessLockPinSalt)).GetString());
        Assert.NotEqual(pin, settings.GetProperty(nameof(CliptonSettings.HistoryAccessLockPinHash)).GetString());
        Assert.DoesNotContain(pin, settingsJson, StringComparison.Ordinal);
        Assert.True(runtime.UnlockHistoryAccess(pin));
    }

    [Fact]
    public void ResetHistoryAccessLockAndClearProtectedData_RemovesCredentialHistoryAndSnippets()
    {
        var root = CreateTestRoot();
        var historyPath = Path.Combine(root, "history.dat");
        var snippetsPath = Path.Combine(root, "snippets.json");
        var store = new EncryptedHistoryStore(historyPath);
        store.Save([new ClipboardSnapshot(
            "history-1",
            DateTimeOffset.UtcNow,
            [ClipboardFormatKind.Text],
            text: "private clipboard text")]);
        using var runtime = new CliptonRuntime(root, isSafeMode: true);
        runtime.UpsertSnippet("Secrets", "ApiKey", "private snippet text");
        runtime.ConfigureHistoryAccessLock("2468", timeoutMinutes: 15);

        Assert.Contains("private snippet text", File.ReadAllText(snippetsPath), StringComparison.Ordinal);

        runtime.ResetHistoryAccessLockAndClearProtectedData();

        Assert.False(runtime.IsHistoryAccessLockConfigured);
        Assert.False(runtime.IsHistoryAccessLockEnabled);
        Assert.Empty(runtime.History.Items);
        Assert.Empty(new EncryptedHistoryStore(historyPath).Load());
        Assert.Empty(runtime.Snippets.Snippets);

        var persistedSnippetsJson = File.ReadAllText(snippetsPath);
        Assert.DoesNotContain("private snippet text", persistedSnippetsJson, StringComparison.Ordinal);
        Assert.Empty(JsonSerializer.Deserialize<Snippet[]>(persistedSnippetsJson) ?? []);

        var loadedSettings = new JsonSettingsStore(Path.Combine(root, "settings.json")).Load();
        Assert.False(loadedSettings.HistoryAccessLockEnabled);
        Assert.Equal(string.Empty, loadedSettings.HistoryAccessLockPinSalt);
        Assert.Equal(string.Empty, loadedSettings.HistoryAccessLockPinHash);
        Assert.Equal(HistoryAccessLockCredential.DefaultTimeoutMinutes, loadedSettings.HistoryAccessLockTimeoutMinutes);
    }

    [Fact]
    public void ProtectedDataOperations_RequireUnlockedHistoryAccess()
    {
        var root = CreateTestRoot();
        var historyPath = Path.Combine(root, "history.dat");
        var store = new EncryptedHistoryStore(historyPath);
        store.Save([new ClipboardSnapshot(
            "history-1",
            DateTimeOffset.UtcNow,
            [ClipboardFormatKind.Text],
            text: "private clipboard text")]);
        using var runtime = new CliptonRuntime(root, isSafeMode: true);
        runtime.UpsertSnippet("Secrets", "ApiKey", "private snippet text");
        runtime.ConfigureHistoryAccessLock("2468", timeoutMinutes: 15);
        runtime.LockHistoryAccess();
        var historyExportPath = Path.Combine(root, "history-export.json");
        var snippetsExportPath = Path.Combine(root, "snippets-export.json");

        Assert.True(runtime.RequiresHistoryAccessUnlock);
        Assert.Empty(runtime.CreateHistoryContextOptions("history-1"));
        Assert.Throws<InvalidOperationException>(() => runtime.ExportHistory(historyExportPath));
        Assert.Throws<InvalidOperationException>(() => runtime.ExportSnippets(snippetsExportPath));
        Assert.Throws<InvalidOperationException>(() => runtime.UpsertSnippet("Secrets", "Other", "new private snippet"));
        Assert.False(File.Exists(historyExportPath));
        Assert.False(File.Exists(snippetsExportPath));

        Assert.True(runtime.UnlockHistoryAccess("2468"));

        Assert.NotEmpty(runtime.CreateHistoryContextOptions("history-1"));
        Assert.Equal(1, runtime.ExportHistory(historyExportPath));
        Assert.Equal(1, runtime.ExportSnippets(snippetsExportPath));
        Assert.Contains("private clipboard text", File.ReadAllText(historyExportPath), StringComparison.Ordinal);
        Assert.Contains("private snippet text", File.ReadAllText(snippetsExportPath), StringComparison.Ordinal);
    }

    private static string CreateTestRoot()
    {
        return Path.Combine(Path.GetTempPath(), "clipton-winui-lock-privacy-tests", Guid.NewGuid().ToString("N"));
    }
}
