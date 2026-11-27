// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Codecs.Jpeg;
using Mediana.IO;

namespace Mediana.Images.Metadata
{
    internal static class RawJpegHeaderReader
    {
        private static readonly JpegDecodeOptions Options = RawJpegPreviews.CreateOptions(1);

        internal static async ValueTask<(int Width, int Height)> ReadStoredDimensionsAsync(IRandomAccessByteSource source,
            long offset, int length, CancellationToken token)
        {
            byte[] buffer = new byte[Math.Min(1024, length)];
            int window = -1, valid = 0;
            async ValueTask<ReadOnlyMemory<byte>> ReadAsync(int at, int count)
            {
                token.ThrowIfCancellationRequested();
                if (at < 0 || count > length - at) { throw new InvalidDataException("Truncated embedded JPEG header."); }
                if (window < 0 || at < window || at - window > valid - count)
                {
                    if (count > buffer.Length) { buffer = new byte[count]; }
                    valid = Math.Min(buffer.Length, length - at);
                    window = at;
                    await source.ReadExactlyAtAsync(offset + at, buffer.AsMemory(0, valid), token).ConfigureAwait(false);
                }
                return buffer.AsMemory(at - window, count);
            }

            if (BinaryPrimitives.ReadUInt16BigEndian((await ReadAsync(length - 2, 2).ConfigureAwait(false)).Span) != 0xffd9 ||
                BinaryPrimitives.ReadUInt16BigEndian((await ReadAsync(0, 2).ConfigureAwait(false)).Span) != 0xffd8)
            { throw new InvalidDataException("Embedded JPEG must begin with SOI and end with EOI."); }
            int position = 2;
            for (int i = 1; i < Options.MaximumMarkers; i++)
            {
                if ((await ReadAsync(position++, 1).ConfigureAwait(false)).Span[0] != 0xff)
                { throw new InvalidDataException("JPEG marker prefix is missing."); }
                byte marker;
                do { marker = (await ReadAsync(position++, 1).ConfigureAwait(false)).Span[0]; } while (marker == 0xff);
                if (marker is 0 or 1 or >= 208 and <= 218)
                { throw new InvalidDataException("Embedded JPEG has no valid frame header before its first scan."); }
                int segment = BinaryPrimitives.ReadUInt16BigEndian((await ReadAsync(position, 2).ConfigureAwait(false)).Span);
                if (segment < 2 || segment > length - position) { throw new InvalidDataException("JPEG segment exceeds the embedded image."); }
                if (marker is 192 or 193 or 194)
                {
                    ReadOnlyMemory<byte> frame = await ReadAsync(position + 2, segment - 2).ConfigureAwait(false);
                    using JpegDocument image = new(default, Options, null, null, token);
                    image.ReadFrame(marker, frame.Span, null);
                    return (image.Width, image.Height);
                }
                position += segment;
            }
            throw new InvalidDataException("JPEG exceeds MaximumMarkers.");
        }
    }
}
