using System.Buffers.Binary;
using Clipton.WinUI;

namespace Clipton.WinUI.Tests;

public sealed class NativeClipboardReaderTests
{
    [Fact]
    public void BuildBitmapFileFromDib_ConvertsInfoHeaderAndPreservesPixelOffset()
    {
        var dib = new byte[44];
        BinaryPrimitives.WriteUInt32LittleEndian(dib, 40);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(4), 1);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(14), 24);
        BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(16), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(20), 4);
        dib[40] = 0x11;
        dib[41] = 0x22;
        dib[42] = 0x33;
        dib[43] = 0x00;

        var bitmap = NativeClipboardReader.BuildBitmapFileFromDib(dib);

        Assert.NotNull(bitmap);
        Assert.Equal((byte)'B', bitmap[0]);
        Assert.Equal((byte)'M', bitmap[1]);
        Assert.Equal(58u, BinaryPrimitives.ReadUInt32LittleEndian(bitmap.AsSpan(2)));
        Assert.Equal(54u, BinaryPrimitives.ReadUInt32LittleEndian(bitmap.AsSpan(10)));
        Assert.Equal(dib, bitmap.AsSpan(14).ToArray());
    }

    [Fact]
    public void BuildBitmapFileFromDib_ConvertsCoreHeaderWithRgbTriplePalette()
    {
        var dib = new byte[22];
        BinaryPrimitives.WriteUInt32LittleEndian(dib, 12);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(6), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(10), 1);
        dib[12] = 0xff;
        dib[13] = 0xff;
        dib[14] = 0xff;
        dib[15] = 0xff;
        dib[16] = 0xff;
        dib[17] = 0xff;

        var bitmap = NativeClipboardReader.BuildBitmapFileFromDib(dib);

        Assert.NotNull(bitmap);
        Assert.Equal(32u, BinaryPrimitives.ReadUInt32LittleEndian(bitmap.AsSpan(10)));
    }

    [Theory]
    [InlineData(11)]
    [InlineData(39)]
    public void BuildBitmapFileFromDib_RejectsTruncatedHeaders(int length)
    {
        Assert.Null(NativeClipboardReader.BuildBitmapFileFromDib(new byte[length]));
    }

    [Fact]
    public void BuildBitmapFileFromDib_RejectsUnsupportedDepthAndOversizedDimensions()
    {
        var unsupportedDepth = CreateInfoHeader(1, 1, 2);
        var oversized = CreateInfoHeader(8000, 5000, 24);

        Assert.Null(NativeClipboardReader.BuildBitmapFileFromDib(unsupportedDepth));
        Assert.Null(NativeClipboardReader.BuildBitmapFileFromDib(oversized));
    }

    [Fact]
    public void BuildBitmapFileFromDib_RejectsInvalidExternalMasksAndProfileBounds()
    {
        var invalidMasks = CreateInfoHeader(1, 1, 32);
        BinaryPrimitives.WriteUInt32LittleEndian(invalidMasks.AsSpan(16), 3); // BI_BITFIELDS requires three masks.

        var invalidProfile = new byte[128];
        BinaryPrimitives.WriteUInt32LittleEndian(invalidProfile, 124);
        BinaryPrimitives.WriteInt32LittleEndian(invalidProfile.AsSpan(4), 1);
        BinaryPrimitives.WriteInt32LittleEndian(invalidProfile.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(invalidProfile.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(invalidProfile.AsSpan(14), 32);
        BinaryPrimitives.WriteUInt32LittleEndian(invalidProfile.AsSpan(112), 124);
        BinaryPrimitives.WriteUInt32LittleEndian(invalidProfile.AsSpan(116), 32);

        Assert.Null(NativeClipboardReader.BuildBitmapFileFromDib(invalidMasks));
        Assert.Null(NativeClipboardReader.BuildBitmapFileFromDib(invalidProfile));
    }

    private static byte[] CreateInfoHeader(int width, int height, ushort bitsPerPixel)
    {
        var dib = new byte[40];
        BinaryPrimitives.WriteUInt32LittleEndian(dib, 40);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(4), width);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(8), height);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(14), bitsPerPixel);
        return dib;
    }
}
