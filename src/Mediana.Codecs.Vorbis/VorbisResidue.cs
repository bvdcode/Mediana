// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core.Memory;
using Mediana.Formats.Audio;

namespace Mediana.Codecs.Vorbis
{
    internal class VorbisResidue : IDisposable
    {
        private readonly WritableMediaBuffer storage;
        private readonly int type, begin, end, partitionSize, classifications, classbook, stages;
        public bool IsInterleaved => type == 2;

        public VorbisResidue(scoped VorbisResidueDefinition definition, IReadOnlyList<VorbisCodebook> books, MediaBufferAllocator allocator)
        {
            type = definition.Type; begin = definition.Begin; end = definition.End; partitionSize = definition.PartitionSize;
            classifications = definition.Classifications; classbook = definition.Classbook;
            for (int index = 0; index < definition.Books.Length; index++)
            {
                int book = definition.Books[index];
                if (book < 0) { continue; }
                if (!books[book].HasVectors) { throw new InvalidDataException("A Vorbis residue stage codebook has no vector lookup."); }
                stages = Math.Max(stages, index % 8 + 1);
            }
            storage = allocator.Allocate(checked(definition.Books.Length * sizeof(int)));
            definition.Books.CopyTo(MemoryMarshal.Cast<byte, int>(storage.Span));
        }

        public void Decode(ref VorbisBitReader reader, IReadOnlyList<VorbisCodebook> books, Span<float> spectrum,
            int stride, int bins, scoped ReadOnlySpan<int> channels, scoped Span<byte> classes, Span<float> vector, CancellationToken token)
        {
            if (channels.IsEmpty) { return; }
            int width = type == 2 ? checked(bins * channels.Length) : bins;
            int partitions = (Math.Min(end, width) - begin) / partitionSize;
            if (partitions <= 0 || stages == 0) { return; }
            int codedChannels = type == 2 ? 1 : channels.Length;
            if (classes.Length < codedChannels * partitions) { throw new ArgumentException("The Vorbis residue class workspace is too short.", nameof(classes)); }
            VorbisCodebook phrase = books[classbook];
            ReadOnlySpan<int> stageBooks = MemoryMarshal.Cast<byte, int>(storage.Span);
            for (int stage = 0; stage < stages; stage++)
            {
                for (int partition = 0; partition < partitions;)
                {
                    token.ThrowIfCancellationRequested();
                    if (stage == 0)
                    {
                        for (int channel = 0; channel < codedChannels; channel++)
                        {
                            if (!phrase.TryDecode(ref reader, out int word)) { return; }
                            Span<byte> target = classes.Slice(channel * partitions + partition, Math.Min(phrase.Dimensions, partitions - partition));
                            target.Clear();
                            for (int index = phrase.Dimensions - 1; index >= 0 && word != 0; index--)
                            {
                                if (index < target.Length) { target[index] = (byte)(word % classifications); }
                                word /= classifications;
                            }
                            if (word != 0) { throw new InvalidDataException("A Vorbis residue classword exceeds its classification domain."); }
                        }
                    }
                    int stop = partition + Math.Min(phrase.Dimensions, partitions - partition);
                    for (; partition < stop; partition++)
                    {
                        int offset = checked(begin + partition * partitionSize);
                        for (int channel = 0; channel < codedChannels; channel++)
                        {
                            int bookIndex = stageBooks[classes[channel * partitions + partition] * 8 + stage];
                            if (bookIndex < 0) { continue; }
                            VorbisCodebook book = books[bookIndex];
                            bool complete = type switch
                            {
                                0 => DecodeTransposed(ref reader, book, spectrum.Slice(channels[channel] * stride + offset, partitionSize), vector),
                                1 => DecodeSequential(ref reader, book, spectrum.Slice(channels[channel] * stride + offset, partitionSize), vector),
                                2 => DecodeInterleaved(ref reader, book, spectrum, stride, channels, offset, vector),
                                _ => throw new InvalidDataException("The Vorbis residue type is invalid.")
                            };
                            if (!complete) { return; }
                        }
                    }
                }
            }
        }

        private static bool DecodeTransposed(ref VorbisBitReader reader, VorbisCodebook book, Span<float> output, Span<float> vector)
        {
            if (book.UsedEntries == 0) { return true; }
            int step = output.Length / book.Dimensions;
            Span<int> entries = stackalloc int[step];
            for (int index = 0; index < step; index++) { if (!book.TryDecode(ref reader, out entries[index])) { return false; } }
            for (int index = 0; index < step; index++)
            {
                book.ReadVector(entries[index], vector);
                for (int dimension = 0; dimension < book.Dimensions; dimension++) { output[dimension * step + index] += vector[dimension]; }
            }
            return true;
        }

        private static bool DecodeSequential(ref VorbisBitReader reader, VorbisCodebook book, Span<float> output, Span<float> vector)
        {
            if (book.UsedEntries == 0) { return true; }
            for (int at = 0; at < output.Length;)
            {
                if (!book.TryDecode(ref reader, out int entry)) { return false; }
                book.ReadVector(entry, vector);
                int count = Math.Min(book.Dimensions, output.Length - at);
                for (int index = 0; index < count; index++) { output[at++] += vector[index]; }
            }
            return true;
        }

        private bool DecodeInterleaved(ref VorbisBitReader reader, VorbisCodebook book, Span<float> output,
            int stride, scoped ReadOnlySpan<int> channels, int offset, Span<float> vector)
        {
            if (book.UsedEntries == 0) { return true; }
            int sample = offset / channels.Length, channel = 0, stop = (offset + partitionSize) / channels.Length;
            while (sample < stop)
            {
                if (!book.TryDecode(ref reader, out int entry)) { return false; }
                book.ReadVector(entry, vector);
                for (int index = 0; index < book.Dimensions && sample < stop; index++)
                {
                    output[channels[channel] * stride + sample] += vector[index];
                    if (++channel == channels.Length) { channel = 0; sample++; }
                }
            }
            return true;
        }

        public void Dispose() => storage.Dispose();
    }
}
