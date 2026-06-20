using System.Text;
using Clipton.Core;
using Clipton.WinUI;

namespace Clipton.WinUI.Tests;

public sealed class EncryptedHistoryStorePrivacyTests
{
    [Fact]
    public void Save_DoesNotPersistClipboardPayloadAsPlainText()
    {
        var root = CreateTestRoot();
        var path = Path.Combine(root, "history.dat");
        var store = new EncryptedHistoryStore(path);
        const string secretText = "clipton-privacy-secret-text-9d0e34b9";
        const string secretWindowTitle = "clipton-private-window-title-4c0f31d2";

        store.Save([new ClipboardSnapshot(
            "privacy-history-1",
            DateTimeOffset.UtcNow,
            [ClipboardFormatKind.Text],
            text: secretText,
            sourceApplicationName: "private-app",
            sourceWindowTitle: secretWindowTitle)]);

        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var bytes = File.ReadAllBytes(file);
            Assert.False(ContainsBytes(bytes, Encoding.UTF8.GetBytes(secretText)), $"Plain clipboard text leaked into {file}.");
            Assert.False(ContainsBytes(bytes, Encoding.UTF8.GetBytes(secretWindowTitle)), $"Plain source metadata leaked into {file}.");
        }
    }

    private static bool ContainsBytes(byte[] haystack, byte[] needle)
    {
        if (needle.Length == 0 || haystack.Length < needle.Length)
        {
            return false;
        }

        for (var index = 0; index <= haystack.Length - needle.Length; index++)
        {
            if (haystack.AsSpan(index, needle.Length).SequenceEqual(needle))
            {
                return true;
            }
        }

        return false;
    }

    private static string CreateTestRoot()
    {
        return Path.Combine(Path.GetTempPath(), "clipton-winui-history-privacy-tests", Guid.NewGuid().ToString("N"));
    }
}
