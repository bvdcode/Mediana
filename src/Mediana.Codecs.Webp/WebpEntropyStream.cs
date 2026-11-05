// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using System.Runtime.InteropServices;
using Mediana.Core.Memory;

namespace Mediana.Codecs.Webp
{
    internal class WebpEntropyStream : IDisposable
    {
        private const int HashBits = 16;
        private const int MaximumLength = 4096;
        private const int MaximumDistance = (1 << 20) - 120;
        private const int ChainBits = 16;
        private const int ChainSize = 1 << ChainBits;
        private const int MaximumCandidates = 8;
        private readonly WebpTokenBuffer tokens;
        private readonly int[][] histograms = [new int[280], new int[256], new int[256], new int[256], new int[40]];
        private readonly WebpHuffmanCode[] codes = new WebpHuffmanCode[5];
        private long extraBits;

        private WebpEntropyStream(MediaBufferAllocator allocator) => tokens = new(allocator);

        internal static WebpEntropyStream Create(ReadOnlySpan<uint> pixels, int width, MediaBufferAllocator allocator, CancellationToken token)
        {
            WebpEntropyStream result = new(allocator);
            bool complete = false;
            try
            {
                if (pixels.IndexOfAnyExcept(pixels[0]) < 0)
                {
                    result.Literal(pixels[0]);
                }
                else
                {
                    result.Tokenize(pixels, width, allocator, token);
                }
                for (int channel = 0; channel < result.codes.Length; channel++) { result.codes[channel] = new(result.histograms[channel]); }
                complete = true;
                return result;
            }
            finally
            {
                if (!complete) { result.Dispose(); }
            }
        }

        internal long GetBitCount(bool mainImage)
        {
            WebpBitWriter counter = new();
            WriteHeader(counter, mainImage);
            long bits = counter.BitCount + extraBits;
            for (int channel = 0; channel < codes.Length; channel++) { bits += codes[channel].DataBits(histograms[channel]); }
            return bits;
        }

        internal void Write(WebpBitWriter output, bool mainImage, CancellationToken token)
        {
            WriteHeader(output, mainImage);
            for (int block = 0; block < tokens.BlockCount; block++)
            {
                token.ThrowIfCancellationRequested();
                foreach (ulong value in tokens.GetBlock(block))
                {
                    int length = (int)(value >> 32);
                    if (length == 0)
                    {
                        uint pixel = (uint)value;
                        codes[0].WriteSymbol(output, (byte)(pixel >> 8));
                        codes[1].WriteSymbol(output, (byte)pixel);
                        codes[2].WriteSymbol(output, (byte)(pixel >> 16));
                        codes[3].WriteSymbol(output, (byte)(pixel >> 24));
                    }
                    else
                    {
                        WebpLengthCode run = WebpLengthCode.FromValue(length);
                        WebpLengthCode distance = WebpLengthCode.FromValue((int)(uint)value);
                        codes[0].WriteSymbol(output, 256 + run.Symbol);
                        output.Write(run.Extra, run.ExtraBits);
                        codes[4].WriteSymbol(output, distance.Symbol);
                        output.Write(distance.Extra, distance.ExtraBits);
                    }
                }
            }
        }

        private void WriteHeader(WebpBitWriter output, bool mainImage)
        {
            output.Write(0, 1);
            if (mainImage) { output.Write(0, 1); }
            foreach (WebpHuffmanCode code in codes) { code.WriteHeader(output); }
        }

        private void Tokenize(ReadOnlySpan<uint> pixels, int width, MediaBufferAllocator allocator, CancellationToken token)
        {
            using WritableMediaBuffer storage = allocator.Allocate((1 << HashBits) * sizeof(int));
            using WritableMediaBuffer links = allocator.Allocate(ChainSize * sizeof(int));
            Span<int> positions = MemoryMarshal.Cast<byte, int>(storage.Memory.Span);
            Span<int> previous = MemoryMarshal.Cast<byte, int>(links.Memory.Span);
            int position = 0;
            int nextCheck = 0;
            while (position < pixels.Length)
            {
                if (position >= nextCheck) { token.ThrowIfCancellationRequested(); nextCheck = position + 4096; }
                int length = 0;
                int distance = 0;
                if (position + 2 < pixels.Length)
                {
                    int hash = Hash(pixels, position);
                    int candidate = positions[hash] - 1;
                    previous[position & (ChainSize - 1)] = candidate + 1;
                    positions[hash] = position + 1;
                    int maximum = Math.Min(MaximumLength, pixels.Length - position);
                    for (int attempt = 0; attempt < MaximumCandidates && candidate >= 0 && position - candidate <= MaximumDistance; attempt++)
                    {
                        if (length == maximum) { break; }
                        if (pixels[position + length] == pixels[candidate + length])
                        {
                            int found = Match(pixels, position, candidate, maximum);
                            if (found > length) { length = found; distance = position - candidate; }
                        }
                        if (position - candidate >= ChainSize) { break; }
                        int next = previous[candidate & (ChainSize - 1)] - 1;
                        if (next >= candidate) { break; }
                        candidate = next;
                    }
                    if (length != maximum && position >= width && distance != width)
                    {
                        int vertical = Match(pixels, position, position - width, maximum);
                        if (vertical > length) { length = vertical; distance = width; }
                    }
                    if (length != maximum && position > 0 && distance != 1)
                    {
                        int repeated = Match(pixels, position, position - 1, maximum);
                        if (repeated > length) { length = repeated; distance = 1; }
                    }
                }
                if (length >= 3)
                {
                    int code = distance == width ? 1 : distance == 1 ? 2 : distance + 120;
                    WebpLengthCode run = WebpLengthCode.FromValue(length);
                    WebpLengthCode offset = WebpLengthCode.FromValue(code);
                    tokens.Add(((ulong)length << 32) | (uint)code);
                    histograms[0][256 + run.Symbol]++;
                    histograms[4][offset.Symbol]++;
                    extraBits += run.ExtraBits + offset.ExtraBits;
                    int end = position + length;
                    for (int index = position + 1; index < end && index + 1 < pixels.Length; index++)
                    {
                        int hash = Hash(pixels, index);
                        previous[index & (ChainSize - 1)] = positions[hash];
                        positions[hash] = index + 1;
                    }
                    position = end;
                }
                else
                {
                    Literal(pixels[position++]);
                }
            }
        }

        private void Literal(uint pixel)
        {
            tokens.Add(pixel);
            histograms[0][(byte)(pixel >> 8)]++;
            histograms[1][(byte)pixel]++;
            histograms[2][(byte)(pixel >> 16)]++;
            histograms[3][(byte)(pixel >> 24)]++;
        }

        private static int Hash(ReadOnlySpan<uint> pixels, int position) =>
            (int)(unchecked(pixels[position] * 0x1e35a7bd + BitOperations.RotateLeft(pixels[position + 1], 13) * 0x9e3779b1) >> (32 - HashBits));

        private static int Match(ReadOnlySpan<uint> pixels, int position, int candidate, int maximum)
        {
            if (pixels[position] != pixels[candidate] || pixels[position + 1] != pixels[candidate + 1]
                || pixels[position + 2] != pixels[candidate + 2]) { return 0; }
            return MemoryMarshal.AsBytes(pixels.Slice(position, maximum))
                .CommonPrefixLength(MemoryMarshal.AsBytes(pixels.Slice(candidate, maximum))) / sizeof(uint);
        }

        public void Dispose() => tokens.Dispose();
    }
}
