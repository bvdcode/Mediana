// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core.Memory;
using Mediana.Formats.Audio;

namespace Mediana.Codecs.Vorbis
{
    internal class VorbisFloor0 : VorbisFloor
    {
        private readonly VorbisFloor0Definition definition;
        private readonly WritableMediaBuffer storage;
        private readonly int smallBins, largeBins, bookCount;

        public VorbisFloor0(VorbisFloor0Definition definition, ReadOnlySpan<byte> books,
            VorbisIdentification identification, IReadOnlyList<VorbisCodebook> codebooks, MediaBufferAllocator allocator)
        {
            if (definition.Order < 1 || definition.Rate < 1 || definition.BarkMapSize < 1)
            {
                throw new InvalidDataException("The Vorbis floor 0 order, rate and Bark map must be positive.");
            }
            foreach (byte book in books)
            {
                if (!codebooks[book].HasVectors) { throw new InvalidDataException("A Vorbis floor 0 codebook has no vector lookup."); }
            }
            this.definition = definition; smallBins = identification.SmallBlockSize / 2; largeBins = identification.LargeBlockSize / 2;
            bookCount = books.Length;
            storage = allocator.Allocate(checked((smallBins + largeBins) * sizeof(float) + bookCount));
            Span<float> frequencies = MemoryMarshal.Cast<byte, float>(storage.Span[..((smallBins + largeBins) * sizeof(float))]);
            InitializeFrequencies(frequencies[..smallBins]); InitializeFrequencies(frequencies[smallBins..]);
            books.CopyTo(storage.Span[((smallBins + largeBins) * sizeof(float))..]);
        }

        public override bool Decode(ref VorbisBitReader reader, IReadOnlyList<VorbisCodebook> books,
            Span<float> curve, Span<float> vector, CancellationToken token)
        {
            curve.Clear();
            int bits = definition.AmplitudeBits;
            if (!reader.TryRead(Math.Min(bits, 32), out uint low)) { return false; }
            ulong raw = low;
            if (bits > 32)
            {
                if (!reader.TryRead(bits - 32, out uint high)) { return false; }
                raw |= (ulong)high << 32;
            }
            if (raw == 0) { return false; }
            if (!reader.TryRead(VorbisBitReader.ILog((uint)bookCount), out uint selected) || selected >= bookCount) { return false; }
            int offset = (smallBins + largeBins) * sizeof(float);
            VorbisCodebook book = books[storage.Span[offset + (int)selected]];
            Span<float> lsp = stackalloc float[255];
            float last = 0;
            for (int at = 0; at < definition.Order;)
            {
                if (!book.TryDecode(ref reader, out int entry)) { return false; }
                book.ReadVector(entry, vector);
                int count = Math.Min(book.Dimensions, definition.Order - at);
                for (int index = 0; index < count; index++) { lsp[at++] = vector[index] + last; }
                last = lsp[at - 1];
            }
            float amplitude = (float)raw / (float)(Math.ScaleB(1, bits) - 1) * definition.AmplitudeOffset;
            for (int index = 0; index < definition.Order; index++) { lsp[index] = (float)(2 * Math.Cos(lsp[index])); }
            ReadOnlySpan<float> frequencies = MemoryMarshal.Cast<byte, float>(storage.Span[..offset]);
            ReadOnlySpan<float> map;
            if (curve.Length == smallBins) { map = frequencies[..smallBins]; }
            else if (curve.Length == largeBins) { map = frequencies.Slice(smallBins, largeBins); }
            else { throw new ArgumentException("The Vorbis floor 0 curve has an undeclared block size.", nameof(curve)); }
            for (int index = 0; index < curve.Length;)
            {
                token.ThrowIfCancellationRequested();
                float frequency = map[index], p = .5f, q = .5f;
                int coefficient = 1;
                for (; coefficient < definition.Order; coefficient += 2)
                {
                    q *= frequency - lsp[coefficient - 1]; p *= frequency - lsp[coefficient];
                }
                if (coefficient == definition.Order)
                {
                    q *= frequency - lsp[coefficient - 1]; p *= p * (4 - frequency * frequency); q *= q;
                }
                else { p *= p * (2 - frequency); q *= q * (2 + frequency); }
                float gain = (float)Math.Exp((amplitude / Math.Sqrt(p + q) - definition.AmplitudeOffset) * .11512925);
                do { curve[index++] = gain; } while (index < curve.Length && map[index] == frequency);
            }
            return true;
        }

        private void InitializeFrequencies(Span<float> map)
        {
            float scale = (float)(definition.BarkMapSize / Bark(definition.Rate / 2f));
            float step = (float)(Math.PI / definition.BarkMapSize);
            for (int index = 0; index < map.Length; index++)
            {
                int bin = Math.Min(definition.BarkMapSize - 1, (int)Math.Floor(Bark((definition.Rate / 2f) / map.Length * index) * scale));
                map[index] = (float)(2 * Math.Cos(step * bin));
            }
        }

        private static double Bark(float frequency) =>
            13.1f * Math.Atan(.00074f * frequency) + 2.24f * Math.Atan(frequency * frequency * 1.85e-8f) + 1e-4f * frequency;

        public override void Dispose() => storage.Dispose();
    }
}
