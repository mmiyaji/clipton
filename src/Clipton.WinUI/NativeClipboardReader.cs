using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Clipton.Core;

namespace Clipton.WinUI;

/// <summary>Copies supported clipboard formats into managed memory without retaining IDataObject/WinRT views.</summary>
internal static class NativeClipboardReader
{
    private const int CfBitmap = 2;
    private const int CfText = 1;
    private const int CfUnicodeText = 13;
    private const int CfDib = 8;
    private const int CfHdrop = 15;
    private const int CfLocale = 16;
    private const int CfDibV5 = 17;
    private const int BiRgb = 0;
    private const int BiRle8 = 1;
    private const int BiRle4 = 2;
    private const int BiBitfields = 3;
    private const int BiJpeg = 4;
    private const int BiPng = 5;
    private const int BiAlphaBitfields = 6;
    private const uint DibRgbColors = 0;
    private const uint LocaleIDefaultAnsiCodePage = 0x00001004;
    private const int MaxPayloadBytes = 32 * 1024 * 1024;
    private const int MaxImagePixels = 32_000_000;
    private const uint MaxFileCount = 4096;
    private const long MaxTotalFilePathChars = 1_000_000;
    private const int FileHeaderSize = 14;
    private const int BitmapInfoHeaderSize = 40;
    private static readonly int s_rtfFormat = RegisterClipboardFormat("Rich Text Format");
    private static readonly int s_htmlFormat = RegisterClipboardFormat("HTML Format");
    private static readonly int s_pngFormat = RegisterClipboardFormat("PNG");
    private static readonly UTF8Encoding s_utf8 = new(false, false);

    internal static ClipboardSnapshot? Capture()
    {
        var sequenceBefore = GetClipboardSequenceNumber();
        if (!OpenClipboard(IntPtr.Zero))
        {
            throw ClipboardFailure("OpenClipboard failed");
        }

        RawClipboardData raw;
        try
        {
            raw = CopySupportedFormats();
        }
        finally
        {
            if (!CloseClipboard())
            {
                AppDiagnostics.Log(ClipboardFailure("CloseClipboard failed"), "Clipboard capture");
            }
        }

        var sequenceAfter = GetClipboardSequenceNumber();
        if (sequenceBefore != 0 && sequenceAfter != 0 && sequenceBefore != sequenceAfter)
        {
            throw new ExternalException($"Clipboard changed while formats were copied ({sequenceBefore} to {sequenceAfter}).");
        }

        return CreateSnapshot(raw);
    }

    internal static byte[]? BuildBitmapFileFromDib(byte[] dib)
    {
        if (dib.Length < 12 || dib.Length > MaxPayloadBytes)
        {
            return null;
        }

        var headerSize = ReadUInt32(dib, 0);
        long pixelOffset;
        long width;
        long height;
        if (headerSize == 12)
        {
            width = ReadUInt16(dib, 4);
            height = ReadUInt16(dib, 6);
            if (ReadUInt16(dib, 8) != 1)
            {
                return null;
            }

            var bitsPerPixel = ReadUInt16(dib, 10);
            if (!IsSupportedBitDepth(bitsPerPixel))
            {
                return null;
            }

            var paletteEntries = bitsPerPixel <= 8 ? 1L << bitsPerPixel : 0;
            pixelOffset = headerSize + paletteEntries * 3;
        }
        else
        {
            if (headerSize is not (40 or 108 or 124) || headerSize > dib.Length)
            {
                return null;
            }

            width = ReadInt32(dib, 4);
            height = ReadInt32(dib, 8);
            if (ReadUInt16(dib, 12) != 1)
            {
                return null;
            }

            var bitsPerPixel = ReadUInt16(dib, 14);
            var compression = ReadUInt32(dib, 16);
            if (!IsSupportedBitDepth(bitsPerPixel) || !IsSupportedCompression(compression)
                || (compression == BiRle8 && bitsPerPixel != 8)
                || (compression == BiRle4 && bitsPerPixel != 4)
                || ((compression is BiBitfields or BiAlphaBitfields) && bitsPerPixel is not (16 or 32)))
            {
                return null;
            }

            var colorsUsed = ReadUInt32(dib, 32);
            var paletteEntries = colorsUsed != 0
                ? colorsUsed
                : bitsPerPixel <= 8 ? 1u << bitsPerPixel : 0;
            if (bitsPerPixel <= 8 && paletteEntries > (1u << bitsPerPixel))
            {
                return null;
            }

            var externalMasks = headerSize == BitmapInfoHeaderSize
                ? compression == BiAlphaBitfields ? 16L : compression == BiBitfields ? 12L : 0L
                : 0L;
            pixelOffset = (long)headerSize + externalMasks + paletteEntries * 4L;
            if (headerSize == 124)
            {
                var profileOffset = ReadUInt32(dib, 112);
                var profileSize = ReadUInt32(dib, 116);
                if (profileSize != 0)
                {
                    var profileEnd = (long)profileOffset + profileSize;
                    if (profileOffset < pixelOffset || profileEnd > dib.Length)
                    {
                        return null;
                    }

                    if (profileOffset == pixelOffset)
                    {
                        pixelOffset = profileEnd;
                    }
                }
                else if (profileOffset != 0)
                {
                    return null;
                }
            }
        }

        long absoluteHeight;
        try
        {
            absoluteHeight = Math.Abs(height);
        }
        catch (OverflowException)
        {
            return null;
        }
        if (width <= 0 || absoluteHeight <= 0 || width * absoluteHeight > MaxImagePixels
            || pixelOffset < headerSize || pixelOffset > dib.Length)
        {
            return null;
        }

        if (headerSize != 12)
        {
            var compression = ReadUInt32(dib, 16);
            if (compression is BiRgb or BiBitfields or BiAlphaBitfields)
            {
                var bitsPerPixel = ReadUInt16(dib, 14);
                var rowBytes = ((width * bitsPerPixel + 31) / 32) * 4;
                if (pixelOffset + rowBytes * absoluteHeight > dib.Length)
                {
                    return null;
                }
            }
            else
            {
                var compressedBytes = ReadUInt32(dib, 20);
                if (compressedBytes == 0 || pixelOffset + compressedBytes > dib.Length)
                {
                    return null;
                }
            }
        }
        else
        {
            var bitsPerPixel = ReadUInt16(dib, 10);
            var rowBytes = ((width * bitsPerPixel + 31) / 32) * 4;
            if (pixelOffset + rowBytes * absoluteHeight > dib.Length)
            {
                return null;
            }
        }

        var fileSize = (long)FileHeaderSize + dib.Length;
        if (fileSize > MaxPayloadBytes + FileHeaderSize || fileSize > uint.MaxValue)
        {
            return null;
        }

        var bitmapFile = new byte[(int)fileSize];
        bitmapFile[0] = (byte)'B';
        bitmapFile[1] = (byte)'M';
        WriteUInt32(bitmapFile, 2, (uint)fileSize);
        WriteUInt32(bitmapFile, 10, (uint)(FileHeaderSize + pixelOffset));
        Buffer.BlockCopy(dib, 0, bitmapFile, FileHeaderSize, dib.Length);
        return bitmapFile;
    }

    private static RawClipboardData CopySupportedFormats()
    {
        var text = CopyGlobalFormat(CfUnicodeText, "Unicode text", MaxPayloadBytes);
        var ansiText = text is null ? CopyGlobalFormat(CfText, "ANSI text", MaxPayloadBytes) : null;
        var locale = ansiText is null ? null : CopyGlobalFormat(CfLocale, "locale", sizeof(uint));
        var rtfBytes = TryCopyGlobalFormat(s_rtfFormat, "RTF", MaxPayloadBytes);
        var htmlBytes = TryCopyGlobalFormat(s_htmlFormat, "HTML", MaxPayloadBytes);
        var paths = TryCopyFileDrop();
        var images = CopyImageCandidates();
        return new RawClipboardData(text, ansiText, locale, rtfBytes, htmlBytes, paths, images);
    }

    private static ClipboardSnapshot? CreateSnapshot(RawClipboardData raw)
    {
        var formats = new List<ClipboardFormatKind>();
        var text = raw.Text is not null ? DecodeUnicodeText(raw.Text) : DecodeAnsiText(raw.AnsiText, raw.Locale);
        var rtf = DecodeRtf(raw.RtfBytes);
        var html = DecodeHtml(raw.HtmlBytes);
        if (!string.IsNullOrEmpty(text)) formats.Add(ClipboardFormatKind.Text);
        if (!string.IsNullOrEmpty(rtf)) formats.Add(ClipboardFormatKind.RichText);
        if (!string.IsNullOrEmpty(html)) formats.Add(ClipboardFormatKind.Html);
        if (raw.FilePaths is { Count: > 0 }) formats.Add(ClipboardFormatKind.FileDrop);

        if (string.IsNullOrEmpty(text))
        {
            text = ClipboardTextExtraction.ExtractPlainText(rtf, html);
            if (!string.IsNullOrEmpty(text)) formats.Add(ClipboardFormatKind.Text);
        }

        byte[]? imagePng = null;
        foreach (var candidate in raw.Images)
        {
            imagePng = DecodeImageToPng(candidate);
            if (imagePng is { Length: > 0 }) break;
        }
        if (imagePng is { Length: > 0 }) formats.Add(ClipboardFormatKind.Image);

        return formats.Count == 0
            ? null
            : new ClipboardSnapshot(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, formats,
                text, rtf, html, imagePng, raw.FilePaths);
    }

    private static IReadOnlyList<byte[]> CopyImageCandidates()
    {
        var candidates = new List<byte[]>();
        var png = TryCopyGlobalFormat(s_pngFormat, "PNG image", MaxPayloadBytes);
        if (png is not null)
        {
            candidates.Add(png);
        }

        foreach (var format in new[] { CfDibV5, CfDib })
        {
            var dib = TryCopyGlobalFormat(format, format == CfDibV5 ? "DIBV5 image" : "DIB image", MaxPayloadBytes);
            if (dib is not null && BuildBitmapFileFromDib(dib) is { } bitmapFile)
            {
                candidates.Add(bitmapFile);
            }
            else if (dib is not null)
            {
                AppDiagnostics.Info("Clipboard", $"Skipped malformed {FormatName(format)} clipboard image; trying the next image format.");
            }
        }

        if (candidates.Count == 0 && IsClipboardFormatAvailable(CfBitmap))
        {
            try
            {
                var handle = GetClipboardData(CfBitmap);
                if (handle == IntPtr.Zero)
                {
                    throw ClipboardFailure("GetClipboardData(CF_BITMAP) failed");
                }

                var dib = CopyBitmapToDib(handle);
                if (dib is not null && BuildBitmapFileFromDib(dib) is { } bitmapFile)
                {
                    candidates.Add(bitmapFile);
                }
            }
            catch (ExternalException exception)
            {
                AppDiagnostics.Log(exception, "Clipboard CF_BITMAP format");
            }
        }

        return candidates;
    }

    private static byte[]? CopyBitmapToDib(IntPtr bitmapHandle)
    {
        if (GetObject(bitmapHandle, Marshal.SizeOf<BitmapObject>(), out var bitmap) == 0
            || bitmap.Width <= 0 || bitmap.Height <= 0
            || (long)bitmap.Width * bitmap.Height > MaxImagePixels)
        {
            AppDiagnostics.Info("Clipboard", "Skipped CF_BITMAP because its dimensions were invalid or exceeded the image limit.");
            return null;
        }

        long imageBytes;
        try
        {
            imageBytes = checked((long)bitmap.Width * bitmap.Height * 4);
        }
        catch (OverflowException)
        {
            return null;
        }

        if (imageBytes > MaxPayloadBytes)
        {
            AppDiagnostics.Info("Clipboard", "Skipped CF_BITMAP because its 32-bit copy would exceed the image byte limit.");
            return null;
        }

        var header = new BitmapInfoHeader
        {
            Size = BitmapInfoHeaderSize,
            Width = bitmap.Width,
            Height = bitmap.Height,
            Planes = 1,
            BitCount = 32,
            Compression = BiRgb,
            SizeImage = (uint)imageBytes
        };
        var pixels = new byte[(int)imageBytes];
        var dc = CreateCompatibleDC(IntPtr.Zero);
        if (dc == IntPtr.Zero)
        {
            throw ClipboardFailure("CreateCompatibleDC failed");
        }

        try
        {
            var lines = GetDIBits(dc, bitmapHandle, 0, (uint)bitmap.Height, pixels, ref header, DibRgbColors);
            if (lines != bitmap.Height)
            {
                AppDiagnostics.Info("Clipboard", "Skipped CF_BITMAP because GetDIBits did not return all scan lines.");
                return null;
            }
        }
        finally
        {
            DeleteDC(dc);
        }

        var dib = new byte[BitmapInfoHeaderSize + pixels.Length];
        WriteUInt32(dib, 0, BitmapInfoHeaderSize);
        WriteInt32(dib, 4, bitmap.Width);
        WriteInt32(dib, 8, bitmap.Height);
        WriteUInt16(dib, 12, 1);
        WriteUInt16(dib, 14, 32);
        WriteUInt32(dib, 16, BiRgb);
        WriteUInt32(dib, 20, (uint)pixels.Length);
        Buffer.BlockCopy(pixels, 0, dib, BitmapInfoHeaderSize, pixels.Length);
        return dib;
    }

    private static byte[]? CopyGlobalFormat(int format, string name, int maxBytes)
    {
        if (format == 0 || !IsClipboardFormatAvailable(format))
        {
            return null;
        }

        var handle = GetClipboardData(format);
        if (handle == IntPtr.Zero)
        {
            throw ClipboardFailure($"GetClipboardData({name}) failed");
        }

        var size = GlobalSize(handle).ToUInt64();
        if (size == 0)
        {
            return [];
        }

        if (size > (ulong)maxBytes)
        {
            AppDiagnostics.Info("Clipboard", $"Skipped {name} because its clipboard block was {size} bytes (limit {maxBytes}).");
            return null;
        }

        var pointer = GlobalLock(handle);
        if (pointer == IntPtr.Zero)
        {
            throw ClipboardFailure($"GlobalLock({name}) failed");
        }

        try
        {
            var bytes = new byte[(int)size];
            Marshal.Copy(pointer, bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            GlobalUnlock(handle);
        }
    }

    private static byte[]? TryCopyGlobalFormat(int format, string name, int maxBytes)
    {
        try
        {
            return CopyGlobalFormat(format, name, maxBytes);
        }
        catch (ExternalException exception)
        {
            AppDiagnostics.Log(exception, $"Clipboard {name} format");
            return null;
        }
    }

    private static IReadOnlyList<string>? TryCopyFileDrop()
    {
        try
        {
            return CopyFileDrop();
        }
        catch (ExternalException exception)
        {
            AppDiagnostics.Log(exception, "Clipboard file drop format");
            return null;
        }
    }

    private static IReadOnlyList<string>? CopyFileDrop()
    {
        if (!IsClipboardFormatAvailable(CfHdrop))
        {
            return null;
        }

        var handle = GetClipboardData(CfHdrop);
        if (handle == IntPtr.Zero)
        {
            throw ClipboardFailure("GetClipboardData(CF_HDROP) failed");
        }

        var count = DragQueryFile(handle, uint.MaxValue, null, 0);
        if (count == 0)
        {
            return [];
        }

        if (count > MaxFileCount)
        {
            AppDiagnostics.Info("Clipboard", $"Skipped file drop with {count} paths (limit {MaxFileCount}).");
            return null;
        }

        var paths = new List<string>((int)count);
        long totalChars = 0;
        for (uint index = 0; index < count; index++)
        {
            var length = DragQueryFile(handle, index, null, 0);
            if (length == 0 || length > 32767 || totalChars + length > MaxTotalFilePathChars)
            {
                AppDiagnostics.Info("Clipboard", "Skipped file drop because a path was invalid or exceeded the total path limit.");
                return null;
            }

            var buffer = new StringBuilder((int)length + 1);
            var copied = DragQueryFile(handle, index, buffer, (uint)buffer.Capacity);
            if (copied != length)
            {
                throw ClipboardFailure("DragQueryFile could not copy a clipboard path");
            }

            var path = buffer.ToString();
            if (File.Exists(path))
            {
                paths.Add(path);
            }

            totalChars += length;
        }

        return paths;
    }

    private static string? DecodeUnicodeText(byte[] bytes)
    {
        var byteLength = bytes.Length & ~1;
        for (var index = 0; index + 1 < byteLength; index += 2)
        {
            if (bytes[index] == 0 && bytes[index + 1] == 0)
            {
                byteLength = index;
                break;
            }
        }

        return byteLength == 0 ? null : Encoding.Unicode.GetString(bytes, 0, byteLength);
    }

    private static string? DecodeAnsiText(byte[]? bytes, byte[]? localeBytes)
    {
        if (bytes is null) return null;
        var byteLength = Array.IndexOf(bytes, (byte)0);
        if (byteLength < 0) byteLength = bytes.Length;
        var codePage = GetAnsiCodePage(localeBytes);
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(codePage).GetString(bytes, 0, byteLength);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return Encoding.Latin1.GetString(bytes, 0, byteLength);
        }
    }

    private static int GetAnsiCodePage(byte[]? localeBytes)
    {
        if (localeBytes is { Length: >= sizeof(uint) })
        {
            var lcid = ReadUInt32(localeBytes, 0);
            var page = new StringBuilder(16);
            if (GetLocaleInfo(lcid, LocaleIDefaultAnsiCodePage, page, page.Capacity) > 0
                && int.TryParse(page.ToString(), out var localeCodePage))
            {
                return localeCodePage;
            }
        }

        return checked((int)GetACP());
    }

    private static string? DecodeRtf(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0) return null;
        var byteLength = Array.IndexOf(bytes, (byte)0);
        if (byteLength < 0) byteLength = bytes.Length;
        var text = Encoding.Latin1.GetString(bytes, 0, byteLength);
        var match = Regex.Match(text, @"\\ansicpg(?<page>\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success || !int.TryParse(match.Groups["page"].Value, out var codePage))
        {
            return text;
        }

        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(codePage).GetString(bytes, 0, byteLength);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return text;
        }
    }

    private static string? DecodeHtml(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0) return null;
        var byteLength = Array.IndexOf(bytes, (byte)0);
        if (byteLength < 0) byteLength = bytes.Length;
        return byteLength == 0 ? null : s_utf8.GetString(bytes, 0, byteLength);
    }

    internal static byte[]? DecodeImageToPng(byte[] sourceBytes)
    {
        var isPng = HasPngSignature(sourceBytes);
        var isBitmap = sourceBytes.Length >= 2 && sourceBytes[0] == (byte)'B' && sourceBytes[1] == (byte)'M';
        if (isPng && !HasReasonablePngDimensions(sourceBytes))
        {
            AppDiagnostics.Info("Clipboard", "Skipped PNG clipboard image because its dimensions were invalid or exceeded the image limit.");
            return null;
        }

        if (!isPng && (!isBitmap || !HasReasonableBitmapDimensions(sourceBytes)))
        {
            AppDiagnostics.Info("Clipboard", "Skipped clipboard image because it was neither a bounded PNG nor a valid bounded bitmap.");
            return null;
        }

        try
        {
            using var input = new MemoryStream(sourceBytes, writable: false);
            using var image = Image.FromStream(input, useEmbeddedColorManagement: true, validateImageData: true);
            if ((long)image.Width * image.Height > MaxImagePixels)
            {
                AppDiagnostics.Info("Clipboard", $"Skipped clipboard image because its dimensions were {image.Width}x{image.Height}.");
                return null;
            }

            using var output = new MemoryStream();
            image.Save(output, ImageFormat.Png);
            if (output.Length > MaxPayloadBytes)
            {
                AppDiagnostics.Info("Clipboard", $"Skipped clipboard image because its encoded PNG was {output.Length} bytes.");
                return null;
            }

            return output.ToArray();
        }
        catch (Exception exception) when (exception is ArgumentException or ExternalException or OutOfMemoryException)
        {
            AppDiagnostics.Info("Clipboard", "Skipped clipboard image because its native bitmap data could not be decoded.");
            return null;
        }
    }

    private static bool IsSupportedBitDepth(ushort value) => value is 1 or 4 or 8 or 16 or 24 or 32;

    private static bool IsSupportedCompression(uint value) => value is BiRgb or BiRle8 or BiRle4 or BiBitfields or BiJpeg or BiPng or BiAlphaBitfields;

    private static string FormatName(int format) => format == CfDibV5 ? "CF_DIBV5" : "CF_DIB";

    private static bool HasPngSignature(byte[] bytes) => bytes.Length >= 8
        && bytes[0] == 137 && bytes[1] == 80 && bytes[2] == 78 && bytes[3] == 71
        && bytes[4] == 13 && bytes[5] == 10 && bytes[6] == 26 && bytes[7] == 10;

    internal static bool HasReasonablePngDimensions(byte[] bytes)
    {
        if (bytes.Length < 24 || System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(8, 4)) != 13
            || bytes[12] != (byte)'I' || bytes[13] != (byte)'H' || bytes[14] != (byte)'D' || bytes[15] != (byte)'R')
        {
            return false;
        }

        var width = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16, 4));
        var height = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20, 4));
        return width > 0 && height > 0 && (ulong)width * height <= MaxImagePixels;
    }

    private static uint ReadUInt32(byte[] bytes, int offset) => System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, sizeof(uint)));
    private static int ReadInt32(byte[] bytes, int offset) => System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset, sizeof(int)));
    private static ushort ReadUInt16(byte[] bytes, int offset) => System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, sizeof(ushort)));
    private static void WriteUInt32(byte[] bytes, int offset, uint value) => System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset, sizeof(uint)), value);
    private static void WriteInt32(byte[] bytes, int offset, int value) => System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset, sizeof(int)), value);
    private static void WriteUInt16(byte[] bytes, int offset, ushort value) => System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset, sizeof(ushort)), value);

    private static ExternalException ClipboardFailure(string message)
    {
        var error = Marshal.GetLastWin32Error();
        return new ExternalException($"{message}; Win32 error {error}.", Marshal.GetHRForLastWin32Error());
    }

    private static int RegisterClipboardFormat(string name) => RegisterClipboardFormatW(name);

    private static bool HasReasonableBitmapDimensions(byte[] bytes)
    {
        if (bytes.Length < FileHeaderSize + BitmapInfoHeaderSize
            || ReadUInt32(bytes, 2) > bytes.Length
            || ReadUInt32(bytes, 10) < FileHeaderSize + 12
            || ReadUInt32(bytes, 10) >= bytes.Length)
        {
            return false;
        }

        var headerSize = ReadUInt32(bytes, FileHeaderSize);
        if (headerSize == 12)
        {
            if (ReadUInt32(bytes, 10) < FileHeaderSize + 12) return false;
            var coreWidth = ReadUInt16(bytes, FileHeaderSize + 4);
            var coreHeight = ReadUInt16(bytes, FileHeaderSize + 6);
            return coreWidth > 0 && coreHeight > 0 && (long)coreWidth * coreHeight <= MaxImagePixels;
        }

        if (headerSize < BitmapInfoHeaderSize || FileHeaderSize + headerSize > bytes.Length
            || ReadUInt32(bytes, 10) < FileHeaderSize + BitmapInfoHeaderSize) return false;

        var width = ReadInt32(bytes, FileHeaderSize + 4);
        var height = ReadInt32(bytes, FileHeaderSize + 8);
        if (width <= 0) return false;
        var absoluteHeight = Math.Abs((long)height);
        return absoluteHeight > 0 && (long)width * absoluteHeight <= MaxImagePixels;
    }

    private sealed record RawClipboardData(byte[]? Text, byte[]? AnsiText, byte[]? Locale, byte[]? RtfBytes,
        byte[]? HtmlBytes, IReadOnlyList<string>? FilePaths, IReadOnlyList<byte[]> Images);

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapObject
    {
        public int Type;
        public int Width;
        public int Height;
        public int WidthBytes;
        public ushort Planes;
        public ushort BitsPixel;
        public IntPtr Bits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(IntPtr owner);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetClipboardData(int format);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsClipboardFormatAvailable(int format);

    [DllImport("user32.dll", EntryPoint = "RegisterClipboardFormatW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int RegisterClipboardFormatW(string format);

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern UIntPtr GlobalSize(IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr memory);

    [DllImport("kernel32.dll")]
    private static extern uint GetACP();

    [DllImport("kernel32.dll", EntryPoint = "GetLocaleInfoW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetLocaleInfo(uint locale, uint type, StringBuilder value, int count);

    [DllImport("shell32.dll", EntryPoint = "DragQueryFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint DragQueryFile(IntPtr drop, uint file, StringBuilder? fileName, uint capacity);

    [DllImport("gdi32.dll", EntryPoint = "GetObjectW", SetLastError = true)]
    private static extern int GetObject(IntPtr handle, int size, out BitmapObject bitmap);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleDC(IntPtr dc);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr dc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint startScan, uint scanLines, [Out] byte[] bits, ref BitmapInfoHeader info, uint usage);
}
