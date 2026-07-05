using Clipton.WinUI;

namespace Clipton.WinUI.Tests;

public sealed class EncryptedExportFileTests
{
    [Fact]
    public void EnsureFileWithinImportLimit_RejectsOversizedFiles()
    {
        var path = Path.Combine(Path.GetTempPath(), "clipton-export-tests", Guid.NewGuid().ToString("N"), "large.clipton");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var stream = File.Create(path))
        {
            stream.SetLength(EncryptedExportFile.MaxImportFileBytes + 1);
        }

        Assert.Throws<InvalidOperationException>(() => EncryptedExportFile.EnsureFileWithinImportLimit(path));
    }

    [Fact]
    public void EnsureFileWithinImportLimit_AllowsFilesAtLimit()
    {
        var path = Path.Combine(Path.GetTempPath(), "clipton-export-tests", Guid.NewGuid().ToString("N"), "limit.clipton");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var stream = File.Create(path))
        {
            stream.SetLength(EncryptedExportFile.MaxImportFileBytes);
        }

        EncryptedExportFile.EnsureFileWithinImportLimit(path);
    }
}
