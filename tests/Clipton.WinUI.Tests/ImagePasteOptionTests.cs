using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using Clipton.Core;
using Clipton.WinUI;

namespace Clipton.WinUI.Tests;

public sealed class ImagePasteOptionTests
{
    [Fact]
    public void CreateHistoryContextOptions_IncludesResizableImagePasteOption()
    {
        using var runtime = new CliptonRuntime(CreateTestRoot(), isSafeMode: true);
        runtime.History.Add(new ClipboardSnapshot(
            "image-1",
            DateTimeOffset.UtcNow,
            [ClipboardFormatKind.Image],
            imagePng: CreatePngBytes(8, 4)));

        var optionIds = runtime.CreateHistoryContextOptions("image-1")
            .Select(option => option.Id)
            .ToArray();

        Assert.Contains(QuickMenuPasteOptionIds.PasteImageResizeHalf, optionIds);
    }

    [Fact]
    public void CreateHistoryContextOptions_HidesDisabledResizableImagePasteOption()
    {
        using var runtime = new CliptonRuntime(CreateTestRoot(), isSafeMode: true);
        runtime.History.Add(new ClipboardSnapshot(
            "image-1",
            DateTimeOffset.UtcNow,
            [ClipboardFormatKind.Image],
            imagePng: CreatePngBytes(8, 4)));
        runtime.SetQuickMenuPasteOptionEnabled(QuickMenuPasteOptionIds.PasteImageResizeHalf, enabled: false);

        var optionIds = runtime.CreateHistoryContextOptions("image-1")
            .Select(option => option.Id)
            .ToArray();

        Assert.DoesNotContain(QuickMenuPasteOptionIds.PasteImageResizeHalf, optionIds);
    }

    [Fact]
    public void ResizeImagePng_ReducesDimensionsByHalf()
    {
        var method = typeof(CliptonRuntime).GetMethod("ResizeImagePng", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("ResizeImagePng was not found.");
        var resized = (byte[])method.Invoke(null, [CreatePngBytes(8, 4), 0.5d])!;

        using var input = new MemoryStream(resized);
        using var image = Image.FromStream(input);

        Assert.Equal(4, image.Width);
        Assert.Equal(2, image.Height);
    }

    [Fact]
    public void EncodePng_ReturnsEmptyBytesForUndecodableImage()
    {
        var method = typeof(ClipboardBridge).GetMethod("EncodePng", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("EncodePng was not found.");

        var encoded = (byte[])method.Invoke(null, [new byte[] { 1, 2, 3, 4 }])!;

        Assert.Empty(encoded);
    }

    private static string CreateTestRoot()
    {
        return Path.Combine(Path.GetTempPath(), "clipton-winui-image-option-tests", Guid.NewGuid().ToString("N"));
    }

    private static byte[] CreatePngBytes(int width, int height)
    {
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);
            using var brush = new SolidBrush(Color.FromArgb(255, 32, 128, 220));
            graphics.FillRectangle(brush, 0, 0, width, height);
        }

        using var output = new MemoryStream();
        bitmap.Save(output, ImageFormat.Png);
        return output.ToArray();
    }
}
