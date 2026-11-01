using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using Clipton.Core;
using Clipton.WinUI;

namespace Clipton.WinUI.Tests;

public sealed class QuickMenuPerformanceTests
{
    [Fact]
    public void SearchDebounce_UsesMeasuredFastPathOnlyAfterRecentFastSearches()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(150), RichQuickMenuWindow.SearchDebounceInterval(0));
        Assert.Equal(TimeSpan.FromMilliseconds(150), RichQuickMenuWindow.SearchDebounceInterval(1));
        Assert.Equal(TimeSpan.FromMilliseconds(70), RichQuickMenuWindow.SearchDebounceInterval(2));
    }

    [Fact]
    public async Task Thumbnail_SlowProviderDoesNotBlockCallerAndConcurrentRequestsShareWork()
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var calls = 0;
        byte[] bytes = [1, 2, 3];
        var item = new QuickMenuItem("Image", "", "I", "", () => { }, IconImageBytesProvider: () =>
        {
            Interlocked.Increment(ref calls);
            started.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException();
            }
            return bytes;
        });

        Assert.True(item.HasIconImage);
        Assert.Equal(0, calls);
        try
        {
            var first = item.GetIconImageBytesAsync();
            Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
            Assert.False(first.IsCompleted);
            var second = item.GetIconImageBytesAsync();
            Assert.Same(first, second);
            release.Set();
            Assert.Same(bytes, await first);
            Assert.Equal(1, calls);
        }
        finally
        {
            release.Set();
        }
    }

    [Fact]
    public async Task Thumbnail_FailedProviderDoesNotPreventNextImageFromLoading()
    {
        var bad = new QuickMenuItem("Bad", "", "I", "", () => { },
            IconImageBytesProvider: () => throw new InvalidDataException("Invalid image"));
        var good = new QuickMenuItem("Good", "", "I", "", () => { },
            IconImageBytesProvider: () => [7]);

        Assert.Null(await bad.GetIconImageBytesAsync());
        Assert.Equal(new byte[] { 7 }, await good.GetIconImageBytesAsync());
    }

    [Fact]
    public async Task BuildMenu_DefersImageDecodeAndReusesThumbnailWithoutWritingFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "clipton-menu-performance-tests", Guid.NewGuid().ToString("N"));
        using var runtime = new CliptonRuntime(root, isSafeMode: true);
        runtime.Settings.QuickMenuImagePreviewSize = "small";
        using var image = new Bitmap(128, 64);
        using var stream = new MemoryStream();
        image.Save(stream, ImageFormat.Png);
        var snapshot = new ClipboardSnapshot("image", DateTimeOffset.UtcNow,
            [ClipboardFormatKind.Image], imagePng: stream.ToArray());
        runtime.History.Add(snapshot);

        var item = Assert.Single(BuildMenu(runtime), item => item.KindLabel == "I");
        Assert.Null(item.IconImageBytes);
        Assert.True(item.HasIconImage);
        Assert.Same(snapshot.ImagePng, item.PreviewImageBytesProvider!());
        Assert.False(Directory.Exists(Path.Combine(root, "thumbs")));

        var bytes = await item.GetIconImageBytesAsync();
        Assert.NotNull(bytes);
        using var thumbnailStream = new MemoryStream(bytes);
        using var thumbnail = Image.FromStream(thumbnailStream);
        Assert.Equal(96, thumbnail.Width);
        Assert.Equal(96, thumbnail.Height);
        var reopenedItem = Assert.Single(BuildMenu(runtime), item => item.KindLabel == "I");
        Assert.Same(bytes, await reopenedItem.GetIconImageBytesAsync());
        Assert.False(Directory.Exists(Path.Combine(root, "thumbs")));

        runtime.Settings.QuickMenuImagePreviewSize = "none";
        var noThumbnail = Assert.Single(BuildMenu(runtime), item => item.KindLabel == "I");
        Assert.False(noThumbnail.HasIconImage);
        Assert.Null(await noThumbnail.GetIconImageBytesAsync());
        Assert.Same(snapshot.ImagePng, noThumbnail.PreviewImageBytesProvider!());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Thumbnail_PendingMenuItemDoesNotReloadDeletedHistory(bool clearAll)
    {
        var root = Path.Combine(Path.GetTempPath(), "clipton-menu-performance-tests", Guid.NewGuid().ToString("N"));
        using var runtime = new CliptonRuntime(root, isSafeMode: true);
        runtime.Settings.QuickMenuImagePreviewSize = "small";
        using var image = new Bitmap(8, 4);
        using var stream = new MemoryStream();
        image.Save(stream, ImageFormat.Png);
        runtime.History.Add(new ClipboardSnapshot("image", DateTimeOffset.UtcNow,
            [ClipboardFormatKind.Image], imagePng: stream.ToArray()));
        var pendingItem = Assert.Single(BuildMenu(runtime), item => item.KindLabel == "I");

        if (clearAll)
        {
            runtime.ClearHistory();
        }
        else
        {
            runtime.RemoveHistoryItem("image");
        }

        Assert.Null(await pendingItem.GetIconImageBytesAsync());
        Assert.False(Directory.Exists(Path.Combine(root, "thumbs")));
    }

    [Fact]
    public void BuildMenu_RegisteredSnippetKeepsMaskedTitleAndRevealAction()
    {
        var root = Path.Combine(Path.GetTempPath(), "clipton-menu-performance-tests", Guid.NewGuid().ToString("N"));
        using var runtime = new CliptonRuntime(root, isSafeMode: true);
        const string secret = "private registered value";
        runtime.UpsertSnippet("", "Saved label", secret);
        runtime.History.Add(new ClipboardSnapshot("text", DateTimeOffset.UtcNow,
            [ClipboardFormatKind.Text], text: secret));

        var item = Assert.Single(BuildMenu(runtime), item => item.KindLabel == "T");
        Assert.Equal("Saved label", item.Title);
        Assert.Equal(secret, item.RevealedTitle);
        Assert.Equal("\uE8EC", item.IconGlyph);
        Assert.Equal("Segoe Fluent Icons", item.IconFontFamily);
        Assert.NotNull(item.PlainTextInvoke);
    }

    private static IReadOnlyList<QuickMenuItem> BuildMenu(CliptonRuntime runtime)
    {
        var method = typeof(CliptonRuntime).GetMethod("BuildQuickMenuItems", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (IReadOnlyList<QuickMenuItem>)method.Invoke(runtime, null)!;
    }
}
