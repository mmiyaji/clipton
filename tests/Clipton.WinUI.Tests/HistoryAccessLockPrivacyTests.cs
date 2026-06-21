using System.Text.Json;
using System.Text;
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
        var snippetsPath = Path.Combine(root, "snippets.dat");
        var legacySnippetsPath = Path.Combine(root, "snippets.json");
        var store = new EncryptedHistoryStore(historyPath);
        store.Save([new ClipboardSnapshot(
            "history-1",
            DateTimeOffset.UtcNow,
            [ClipboardFormatKind.Text],
            text: "private clipboard text")]);
        using var runtime = new CliptonRuntime(root, isSafeMode: true);
        runtime.UpsertSnippet("Secrets", "ApiKey", "private snippet text");
        runtime.ConfigureHistoryAccessLock("2468", timeoutMinutes: 15);

        Assert.True(File.Exists(snippetsPath));
        Assert.False(File.Exists(legacySnippetsPath));
        Assert.False(ContainsBytes(File.ReadAllBytes(snippetsPath), Encoding.UTF8.GetBytes("private snippet text")));

        runtime.ResetHistoryAccessLockAndClearProtectedData();

        Assert.False(runtime.IsHistoryAccessLockConfigured);
        Assert.False(runtime.IsHistoryAccessLockEnabled);
        Assert.Empty(runtime.History.Items);
        Assert.Empty(new EncryptedHistoryStore(historyPath).Load());
        Assert.Empty(runtime.Snippets.Snippets);

        Assert.True(File.Exists(snippetsPath));
        Assert.False(ContainsBytes(File.ReadAllBytes(snippetsPath), Encoding.UTF8.GetBytes("private snippet text")));
        using (var reloaded = new CliptonRuntime(root, isSafeMode: true))
        {
            Assert.Empty(reloaded.Snippets.Snippets);
        }

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
        var historyImportPath = Path.Combine(root, "history-import.json");
        var snippetsImportPath = Path.Combine(root, "snippets-import.json");
        File.WriteAllText(historyImportPath, """
            {
              "Version": 1,
              "ExportedAt": "2026-06-21T00:00:00+00:00",
              "Items": [
                {
                  "Id": "imported-history",
                  "CapturedAt": "2026-06-21T00:00:00+00:00",
                  "Formats": ["Text"],
                  "Text": "imported private clipboard text"
                }
              ]
            }
            """);
        File.WriteAllText(snippetsImportPath, """
            {
              "Version": 1,
              "ExportedAt": "2026-06-21T00:00:00+00:00",
              "Items": [
                {
                  "Folder": "Secrets",
                  "Name": "Imported",
                  "Text": "imported private snippet text"
                }
              ]
            }
            """);

        Assert.True(runtime.RequiresHistoryAccessUnlock);
        Assert.Empty(runtime.CreateHistoryContextOptions("history-1"));
        Assert.Throws<InvalidOperationException>(() => runtime.ExportHistory(historyExportPath));
        Assert.Throws<InvalidOperationException>(() => runtime.ExportSnippets(snippetsExportPath));
        Assert.Throws<InvalidOperationException>(() => runtime.PreviewImportHistory(historyImportPath));
        Assert.Throws<InvalidOperationException>(() => runtime.PreviewImportSnippets(snippetsImportPath));
        Assert.Throws<InvalidOperationException>(() => runtime.ImportHistory(historyImportPath));
        Assert.Throws<InvalidOperationException>(() => runtime.ImportSnippets(snippetsImportPath));
        Assert.Throws<InvalidOperationException>(() => runtime.UpsertSnippet("Secrets", "Other", "new private snippet"));
        Assert.Throws<InvalidOperationException>(() => runtime.RemoveSnippet("Secrets", "ApiKey"));
        runtime.TogglePinnedHistoryItem("history-1");
        runtime.RemoveHistoryItem("history-1");
        runtime.LoadMorePersistedHistory();
        Assert.False(File.Exists(historyExportPath));
        Assert.False(File.Exists(snippetsExportPath));
        Assert.Single(runtime.History.Items);
        Assert.Single(runtime.Snippets.Snippets);
        Assert.False(runtime.IsHistoryPinned("history-1"));

        Assert.True(runtime.UnlockHistoryAccess("2468"));

        Assert.NotEmpty(runtime.CreateHistoryContextOptions("history-1"));
        Assert.Equal(1, runtime.ExportHistory(historyExportPath));
        Assert.Equal(1, runtime.ExportSnippets(snippetsExportPath));
        Assert.Contains("private clipboard text", File.ReadAllText(historyExportPath), StringComparison.Ordinal);
        Assert.Contains("private snippet text", File.ReadAllText(snippetsExportPath), StringComparison.Ordinal);
        Assert.Equal(1, runtime.ImportHistory(historyImportPath));
        Assert.Equal(1, runtime.ImportSnippets(snippetsImportPath));
        Assert.Contains(runtime.History.Items, item => string.Equals(item.Text, "imported private clipboard text", StringComparison.Ordinal));
        Assert.NotNull(runtime.Snippets.Find("Secrets", "Imported"));
    }

    [Fact]
    public void Snippets_AreMigratedFromPlainJsonToProtectedStore()
    {
        var root = CreateTestRoot();
        var legacyPath = Path.Combine(root, "snippets.json");
        var protectedPath = Path.Combine(root, "snippets.dat");
        Directory.CreateDirectory(root);
        File.WriteAllText(legacyPath, JsonSerializer.Serialize(new[]
        {
            new Snippet("ApiKey", "private snippet text", "Secrets")
        }));

        using (var runtime = new CliptonRuntime(root, isSafeMode: true))
        {
            Assert.NotNull(runtime.Snippets.Find("Secrets", "ApiKey"));
        }

        Assert.False(File.Exists(legacyPath));
        Assert.True(File.Exists(protectedPath));
        Assert.False(ContainsBytes(File.ReadAllBytes(protectedPath), Encoding.UTF8.GetBytes("private snippet text")));

        using var reloaded = new CliptonRuntime(root, isSafeMode: true);
        Assert.Equal("private snippet text", reloaded.Snippets.Find("Secrets", "ApiKey")?.Text);
    }

    [Fact]
    public void SourceMetadata_IsDisabledByDefaultAndScrubbedFromPersistedHistory()
    {
        var root = CreateTestRoot();
        var historyPath = Path.Combine(root, "history.dat");
        var store = new EncryptedHistoryStore(historyPath);
        store.Save([new ClipboardSnapshot(
            "history-1",
            DateTimeOffset.UtcNow,
            [ClipboardFormatKind.Text],
            text: "private clipboard text",
            sourceApplicationName: "SecretApp",
            sourceWindowTitle: "Secret Window")]);

        using var runtime = new CliptonRuntime(root, isSafeMode: true);

        Assert.False(runtime.Settings.SaveHistorySourceMetadata);
        var item = Assert.Single(runtime.History.Items);
        Assert.Null(item.SourceApplicationName);
        Assert.Null(item.SourceWindowTitle);
        var persisted = Assert.Single(store.Load());
        Assert.Null(persisted.SourceApplicationName);
        Assert.Null(persisted.SourceWindowTitle);
    }

    [Fact]
    public void SourceMetadata_CanBePreservedWhenSettingIsEnabled()
    {
        var root = CreateTestRoot();
        Directory.CreateDirectory(root);
        new JsonSettingsStore(Path.Combine(root, "settings.json")).Save(new CliptonSettings
        {
            SaveHistorySourceMetadata = true
        });
        var historyPath = Path.Combine(root, "history.dat");
        var store = new EncryptedHistoryStore(historyPath);
        store.Save([new ClipboardSnapshot(
            "history-1",
            DateTimeOffset.UtcNow,
            [ClipboardFormatKind.Text],
            text: "private clipboard text",
            sourceApplicationName: "SecretApp",
            sourceWindowTitle: "Secret Window")]);

        using var runtime = new CliptonRuntime(root, isSafeMode: true);

        var item = Assert.Single(runtime.History.Items);
        Assert.Equal("SecretApp", item.SourceApplicationName);
        Assert.Equal("Secret Window", item.SourceWindowTitle);

        runtime.SetSaveHistorySourceMetadata(false);
        Assert.Null(Assert.Single(runtime.History.Items).SourceApplicationName);
        Assert.Null(Assert.Single(store.Load()).SourceWindowTitle);
    }

    [Fact]
    public void EncryptedExports_DoNotPersistPayloadAsPlainTextAndRoundTrip()
    {
        var root = CreateTestRoot();
        var historyExportPath = Path.Combine(root, "history.clipton");
        var snippetsExportPath = Path.Combine(root, "snippets.clipton");
        const string password = "correct horse battery staple";
        const string secretHistory = "private clipboard export text";
        const string secretSnippet = "private snippet export text";
        using var runtime = new CliptonRuntime(root, isSafeMode: true);
        runtime.History.Add(new ClipboardSnapshot(
            "history-1",
            DateTimeOffset.UtcNow,
            [ClipboardFormatKind.Text],
            text: secretHistory));
        runtime.UpsertSnippet("Secrets", "ApiKey", secretSnippet);

        Assert.Equal(1, runtime.ExportHistoryEncrypted(historyExportPath, password));
        Assert.Equal(1, runtime.ExportSnippetsEncrypted(snippetsExportPath, password));

        Assert.False(ContainsBytes(File.ReadAllBytes(historyExportPath), Encoding.UTF8.GetBytes(secretHistory)));
        Assert.False(ContainsBytes(File.ReadAllBytes(snippetsExportPath), Encoding.UTF8.GetBytes(secretSnippet)));
        Assert.Throws<InvalidOperationException>(() => runtime.PreviewImportHistoryEncrypted(historyExportPath, "wrong password"));

        var importRoot = CreateTestRoot();
        using var imported = new CliptonRuntime(importRoot, isSafeMode: true);
        Assert.Equal(1, imported.ImportHistoryEncrypted(historyExportPath, password));
        Assert.Equal(1, imported.ImportSnippetsEncrypted(snippetsExportPath, password));
        Assert.Contains(imported.History.Items, item => string.Equals(item.Text, secretHistory, StringComparison.Ordinal));
        Assert.Equal(secretSnippet, imported.Snippets.Find("Secrets", "ApiKey")?.Text);
    }

    private static string CreateTestRoot()
    {
        return Path.Combine(Path.GetTempPath(), "clipton-winui-lock-privacy-tests", Guid.NewGuid().ToString("N"));
    }

    private static bool ContainsBytes(byte[] haystack, byte[] needle)
    {
        if (needle.Length == 0)
        {
            return true;
        }

        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return true;
            }
        }

        return false;
    }
}
