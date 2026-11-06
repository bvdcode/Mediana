// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.IO.Compression;
using Mediana.IO;

namespace Mediana.Codecs.Tiff
{
    internal static class TiffCompression
    {
        internal static void Decode(ReadOnlyMemory<byte> encoded, Span<byte> output, int compression, CancellationToken token)
        {
            switch (compression)
            {
                case 1:
                    if (encoded.Length != output.Length) { throw new InvalidDataException("Uncompressed TIFF block has an invalid length."); }
                    encoded.Span.CopyTo(output); break;
                case 32773: PackBits(encoded.Span, output, token); break;
                case 5: Lzw(encoded.Span, output, token); break;
                case 8:
                case 32946:
                    using (ReadOnlyMemoryStream input = new(encoded))
                    using (ZLibStream zlib = new(input, CompressionMode.Decompress))
                    {
                        int position = 0;
                        while (position < output.Length)
                        {
                            token.ThrowIfCancellationRequested();
                            int read = zlib.Read(output.Slice(position, Math.Min(output.Length - position, 16384)));
                            if (read == 0) { throw new InvalidDataException("Truncated TIFF deflate block."); }
                            position += read;
                        }
                        if (zlib.ReadByte() != -1) { throw new InvalidDataException("TIFF deflate block exceeds its declared samples."); }
                    }
                    break;
                default: throw new NotSupportedException($"TIFF compression {compression} is not supported.");
            }
        }
        private static void PackBits(ReadOnlySpan<byte> input, Span<byte> output, CancellationToken token)
        {
            int position = 0, written = 0;
            while (position < input.Length)
            {
                token.ThrowIfCancellationRequested();
                int header = unchecked((sbyte)input[position++]);
                if (header == -128) { continue; }
                int count = header < 0 ? 1 - header : header + 1;
                if (count > output.Length - written || position >= input.Length) { throw new InvalidDataException("Invalid TIFF PackBits run."); }
                if (header < 0) { output.Slice(written, count).Fill(input[position++]); }
                else
                {
                    if (count > input.Length - position) { throw new InvalidDataException("Truncated TIFF PackBits literal."); }
                    input.Slice(position, count).CopyTo(output[written..]); position += count;
                }
                written += count;
            }
            if (written != output.Length) { throw new InvalidDataException("Incomplete TIFF PackBits block."); }
        }
        private static void Lzw(ReadOnlySpan<byte> input, Span<byte> output, CancellationToken token)
        {
            Span<ushort> prefixes = stackalloc ushort[4096];
            Span<byte> suffixes = stackalloc byte[4096], stack = stackalloc byte[4096];
            for (int i = 0; i < 256; i++) { suffixes[i] = (byte)i; }
            int position = 0, count = 0, width = 9, available = 258, previous = -1, first = 0, written = 0;
            uint bits = 0; bool started = false;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                while (count < width)
                {
                    if (position >= input.Length) { throw new InvalidDataException("TIFF LZW has no end code."); }
                    bits = (bits << 8) | input[position++]; count += 8;
                }
                int code = (int)((bits >> (count - width)) & ((1 << width) - 1)); count -= width;
                if (!started && code != 256) { throw new InvalidDataException("TIFF LZW must start with a clear code."); }
                started = true;
                if (code == 256) { width = 9; available = 258; previous = -1; continue; }
                if (code == 257) { break; }
                int original = code, length = 0;
                if (code == available && previous >= 0) { stack[length++] = (byte)first; code = previous; }
                else if (code >= available) { throw new InvalidDataException("Invalid TIFF LZW dictionary reference."); }
                while (code >= 256)
                {
                    if (code <= 257 || code >= available || length >= stack.Length - 1) { throw new InvalidDataException("Invalid TIFF LZW prefix chain."); }
                    stack[length++] = suffixes[code]; code = prefixes[code];
                }
                first = suffixes[code]; stack[length++] = (byte)first;
                if (length > output.Length - written) { throw new InvalidDataException("TIFF LZW expands beyond its block."); }
                while (length > 0) { output[written++] = stack[--length]; }
                if (previous >= 0 && available < 4096)
                {
                    prefixes[available] = (ushort)previous; suffixes[available++] = (byte)first;
                    if (available == (1 << width) - 1 && width < 12) { width++; }
                }
                previous = original;
            }
            if (written != output.Length) { throw new InvalidDataException("TIFF LZW image is incomplete."); }
        }
    }
}
