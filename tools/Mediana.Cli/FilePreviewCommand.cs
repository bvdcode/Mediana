// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Containers.Mp4;
using Mediana.Core.Video;

namespace Mediana.Cli
{
    internal static class FilePreviewCommand
    {
        private const int ProbeBytes = 512;

        public static async Task<int> RunAsync(string input, string output, int? stream, long milliseconds, int page, CancellationToken token)
        {
            byte[] probe = new byte[ProbeBytes];
            await using FileStream source = File.OpenRead(input);
            int length = await source.ReadAtLeastAsync(probe, ProbeBytes, throwOnEndOfStream: false, cancellationToken: token);
            ReadOnlySpan<byte> prefix = probe.AsSpan(0, length);
            if (prefix.StartsWith("%PDF-"u8))
            {
                return await PdfPreviewCommand.RenderAsync(input, output, page, int.MaxValue, token);
            }
            if (prefix.StartsWith<byte>([0, 0, 0, 12, 0x6a, 0x50, 0x20, 0x20, 13, 10, 0x87, 10]) || prefix.StartsWith<byte>([0xff, 0x4f, 0xff, 0x51]))
            {
                return await ImagePreviewCommand.RasterAsync("jp2", input, output, SampleAspectRatio.Square, token);
            }
            if (prefix.StartsWith<byte>([0x97, 0x4a, 0x42, 0x32, 13, 10, 0x1a, 10]))
            {
                return await ImagePreviewCommand.RasterAsync("jbig2", input, output, SampleAspectRatio.Square, token);
            }
            if (IsHeif(prefix))
            {
                return await ImagePreviewCommand.RasterAsync("heic", input, output, SampleAspectRatio.Square, token);
            }
            if (new Mp4ReaderFactory().CanRead(prefix))
            {
                return await VideoPreviewCommand.RunAsync(input, output, stream, milliseconds, int.MaxValue, int.MaxValue, token);
            }
            if (prefix.StartsWith<byte>([0x1f, 0x8b]) || prefix.TrimStart(" \r\n\t"u8).StartsWith("{"u8))
            {
                return await ImagePreviewCommand.TgsAsync(input, output, milliseconds / 1000.0, null, token);
            }
            if (prefix.StartsWith<byte>([0xef, 0xbb, 0xbf]))
            {
                prefix = prefix[3..];
            }
            if (prefix.TrimStart(" \r\n\t"u8).StartsWith("<"u8))
            {
                return await ImagePreviewCommand.SvgAsync(input, output, null, token);
            }
            throw new NotSupportedException("PNG conversion supports HEIC, JPEG 2000, standalone JBIG2, SVG, TGS/Lottie, PDF and supported MP4/MOV video.");
        }

        private static bool IsHeif(ReadOnlySpan<byte> prefix)
        {
            if (prefix.Length < 16 || !prefix.Slice(4, 4).SequenceEqual("ftyp"u8))
            {
                return false;
            }
            int end = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(prefix), (uint)prefix.Length);
            for (int offset = 8; offset <= end - 4; offset += 4)
            {
                if (offset == 12)
                {
                    continue;
                }
                ReadOnlySpan<byte> brand = prefix.Slice(offset, 4);
                if (brand.SequenceEqual("heic"u8) || brand.SequenceEqual("heix"u8) || brand.SequenceEqual("hevc"u8) ||
                    brand.SequenceEqual("hevx"u8) || brand.SequenceEqual("mif1"u8) || brand.SequenceEqual("msf1"u8))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
