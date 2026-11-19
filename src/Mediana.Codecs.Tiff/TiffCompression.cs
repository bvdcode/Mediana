// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers;
using System.IO.Compression;

namespace Mediana.Codecs.Tiff
{
    internal class TiffCompression : IDisposable
    {
        private const int StepBytes = 16 * 1024;
        private ZLibDecoder? _deflate;

        public void Dispose() => _deflate?.Dispose();

        internal void Decode(ReadOnlyMemory<byte> encoded, Span<byte> output, int compression, CancellationToken token)
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
                    DecodeDeflate(encoded.Span, output, token);
                    break;
                default: throw new NotSupportedException($"TIFF compression {compression} is not supported.");
            }
        }

        private void DecodeDeflate(ReadOnlySpan<byte> source, Span<byte> output, CancellationToken token)
        {
            if (_deflate is null) { _deflate = new(); }
            else { _deflate.Reset(); }
            Span<byte> overflow = stackalloc byte[1];
            while (true)
            {
                token.ThrowIfCancellationRequested();
                bool complete = output.IsEmpty;
                Span<byte> destination = complete ? overflow : output[..Math.Min(output.Length, StepBytes)];
                OperationStatus status = _deflate.Decompress(source[..Math.Min(source.Length, StepBytes)], destination,
                    out int consumed, out int written);
                source = source[consumed..];
                if (complete && written != 0)
                {
                    throw new InvalidDataException("TIFF deflate block exceeds its declared samples.");
                }
                output = output[written..];
                switch (status)
                {
                    case OperationStatus.Done:
                        if (!output.IsEmpty) { throw new InvalidDataException("Incomplete TIFF deflate samples."); }
                        return;
                    case OperationStatus.NeedMoreData:
                        if (source.IsEmpty) { throw new InvalidDataException("Truncated TIFF deflate block."); }
                        break;
                    case OperationStatus.DestinationTooSmall:
                        break;
                    case OperationStatus.InvalidData:
                        throw new InvalidDataException("Invalid TIFF deflate block or checksum.");
                    default:
                        throw new InvalidOperationException("Unknown zlib decoder status.");
                }
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
