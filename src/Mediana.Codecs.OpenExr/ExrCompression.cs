// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers;
using System.IO.Compression;

namespace Mediana.Codecs.OpenExr
{
    internal class ExrCompression : IDisposable
    {
        private const int StepBytes = 16384;
        private ZLibDecoder? zip;
        public void Dispose() => zip?.Dispose();

        internal ReadOnlySpan<byte> Decode(ReadOnlySpan<byte> encoded, Span<byte> workspace, int expected, int compression, CancellationToken token)
        {
            if (encoded.Length == expected) { return encoded; }
            if (encoded.Length > expected || compression == 0) { throw new InvalidDataException("Invalid OpenEXR chunk byte count."); }
            Span<byte> decoded = workspace[..expected];
            switch (compression)
            {
                case 1: UnpackRuns(encoded, decoded, token); break;
                case 2: case 3: Inflate(encoded, decoded, token); break;
                default: throw new NotSupportedException($"OpenEXR compression {compression} is not supported.");
            }
            ExrBytePredictor.Restore(decoded, token);
            Span<byte> output = workspace.Slice(expected, expected);
            ExrBytePredictor.Interleave(decoded, output, token);
            return output;
        }

        private static void UnpackRuns(ReadOnlySpan<byte> input, Span<byte> output, CancellationToken token)
        {
            int source = 0, target = 0;
            while (source < input.Length)
            {
                token.ThrowIfCancellationRequested();
                int code = unchecked((sbyte)input[source++]);
                int count = code < 0 ? -code : code + 1;
                if (count > output.Length - target) { throw new InvalidDataException("OpenEXR RLE exceeds its chunk."); }
                if (code < 0)
                {
                    if (count > input.Length - source) { throw new InvalidDataException("Truncated OpenEXR RLE literal."); }
                    input.Slice(source, count).CopyTo(output[target..]); source += count;
                }
                else
                {
                    if (source == input.Length) { throw new InvalidDataException("Truncated OpenEXR RLE run."); }
                    output.Slice(target, count).Fill(input[source++]);
                }
                target += count;
            }
            if (target != output.Length) { throw new InvalidDataException("Incomplete OpenEXR RLE chunk."); }
        }

        private void Inflate(ReadOnlySpan<byte> input, Span<byte> output, CancellationToken token)
        {
            if (zip is null) { zip = new(); }
            else { zip.Reset(); }
            Span<byte> overflow = stackalloc byte[1];
            while (true)
            {
                token.ThrowIfCancellationRequested();
                bool full = output.IsEmpty;
                OperationStatus status = zip.Decompress(input[..Math.Min(input.Length, StepBytes)],
                    full ? overflow : output[..Math.Min(output.Length, StepBytes)], out int consumed, out int written);
                input = input[consumed..];
                if (full && written != 0) { throw new InvalidDataException("OpenEXR ZIP exceeds its chunk."); }
                output = output[written..];
                switch (status)
                {
                    case OperationStatus.Done:
                        if (!input.IsEmpty || !output.IsEmpty) { throw new InvalidDataException("OpenEXR ZIP size or trailing data is invalid."); }
                        return;
                    case OperationStatus.InvalidData: throw new InvalidDataException("Invalid OpenEXR ZIP stream or checksum.");
                    case OperationStatus.NeedMoreData:
                        if (input.IsEmpty) { throw new InvalidDataException("Truncated OpenEXR ZIP stream."); }
                        break;
                    case OperationStatus.DestinationTooSmall: break;
                    default: throw new InvalidOperationException("Unknown ZIP decoder status.");
                }
            }
        }
    }
}
