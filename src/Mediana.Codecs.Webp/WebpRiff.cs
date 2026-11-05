// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;

namespace Mediana.Codecs.Webp
{
    internal static class WebpRiff
    {
        internal const int HeaderBytes = 12;
        internal const int ChunkHeaderBytes = 8;
        internal const int CanvasChunkBytes = 18;

        internal static int ChunkLength(int length) => checked(ChunkHeaderBytes + length + (length & 1));

        internal static void WriteHeader(Span<byte> output, long fileLength)
        {
            if (fileLength > uint.MaxValue - 1 || fileLength < HeaderBytes || (fileLength & 1) != 0)
            {
                throw new NotSupportedException("WebP file length is outside the RIFF size range.");
            }
            "RIFF"u8.CopyTo(output);
            BinaryPrimitives.WriteUInt32LittleEndian(output[4..], (uint)(fileLength - 8));
            "WEBP"u8.CopyTo(output[8..]);
        }

        internal static void WriteChunk(Span<byte> output, ReadOnlySpan<byte> fourCc, int payloadLength)
        {
            fourCc.CopyTo(output);
            BinaryPrimitives.WriteUInt32LittleEndian(output[4..], (uint)payloadLength);
        }

        internal static void WriteCanvas(Span<byte> output, int width, int height, byte features)
        {
            WriteChunk(output, "VP8X"u8, 10);
            output.Slice(8, 10).Clear();
            output[8] = features;
            Write24(output[12..], width - 1);
            Write24(output[15..], height - 1);
        }

        internal static void Write24(Span<byte> output, int value)
        {
            if ((uint)value > 0xffffff) { throw new ArgumentOutOfRangeException(nameof(value)); }
            output[0] = (byte)value;
            output[1] = (byte)(value >> 8);
            output[2] = (byte)(value >> 16);
        }
    }
}
