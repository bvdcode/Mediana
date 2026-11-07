// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.IO.Compression;
using System.Runtime.InteropServices;
using Mediana.Codecs.Vorbis;
using Mediana.Core.Memory;
using Mediana.Formats.Audio;

namespace Mediana.Tests
{
    internal static class VorbisSpectrumTestInput
    {
        public static int Verify(string root, string filename = "spectrum.bin.gz", bool measureAllocations = false)
        {
            using FileStream file = File.OpenRead(Path.Combine(root, filename));
            using GZipStream gzip = new(file, CompressionMode.Decompress);
            using BinaryReader input = new(gzip);
            if (!input.ReadBytes(8).AsSpan().SequenceEqual("VORSPEC1"u8)) { throw new InvalidDataException("The Vorbis spectrum reference header is invalid."); }
            int streams = input.ReadInt32(), total = 0;
            for (int stream = 0; stream < streams; stream++)
            {
                VorbisIdentification identification = VorbisIdentification.Read(input.ReadBytes(input.ReadInt32()));
                byte[] header = input.ReadBytes(input.ReadInt32());
                int frames = input.ReadInt32(), channels = identification.ChannelCount, maximumBins = identification.LargeBlockSize / 2;
                MediaBufferAllocator allocator = new(32 * 1024 * 1024);
                using (VorbisSetupCapture setup = new(identification, allocator))
                {
                    VorbisSetupInfo.ReadForDecoding(identification, header, setup);
                    int dimension = setup.Books.Max(book => book.Dimensions), capacity = checked(maximumBins * channels);
                    int floats = 2 * capacity + dimension;
                    using WritableMediaBuffer storage = allocator.Allocate(checked(floats * sizeof(float) + capacity));
                    Span<float> work = MemoryMarshal.Cast<byte, float>(storage.Span[..(floats * sizeof(float))]);
                    Span<float> spectrum = work[..capacity], floor = work.Slice(capacity, capacity), vector = work[(2 * capacity)..];
                    Span<byte> classes = storage.Span[(floats * sizeof(float))..];
                    for (int frame = 0; frame < frames; frame++)
                    {
                        byte[] packet = input.ReadBytes(input.ReadInt32());
                        input.ReadInt64(); input.ReadInt32();
                        VorbisBitReader reader = new(packet);
                        if (reader.Read(1) != 0) { throw new InvalidDataException("A reference audio packet has a header flag."); }
                        int modeIndex = (int)reader.Read(VorbisBitReader.ILog((uint)setup.Modes.Count - 1));
                        VorbisMode mode = setup.Modes[modeIndex];
                        int bins = (mode.LongBlock ? identification.LargeBlockSize : identification.SmallBlockSize) / 2;
                        if (mode.LongBlock) { reader.Read(2); }
                        VorbisSpectrum.Decode(ref reader, setup, mode, bins, spectrum, floor, vector, classes, default);
                        if (measureAllocations && frame == 0)
                        {
                            long reserved = allocator.AllocatedBytes;
                            for (int pass = 0; pass < 4; pass++) { Repeat(packet, setup, bins, spectrum, floor, vector, classes); }
                            long before = GC.GetAllocatedBytesForCurrentThread();
                            for (int pass = 0; pass < 16; pass++) { Repeat(packet, setup, bins, spectrum, floor, vector, classes); }
                            if (GC.GetAllocatedBytesForCurrentThread() != before || allocator.AllocatedBytes != reserved)
                            {
                                throw new InvalidDataException("Vorbis spectrum decoding allocated working storage.");
                            }
                        }
                        double energy = 0, error = 0, peak = 0, difference = 0;
                        for (int channel = 0; channel < channels; channel++)
                        {
                            if (input.ReadInt32() != bins) { throw new InvalidDataException("The reference spectrum size differs from its packet mode."); }
                            for (int index = 0; index < bins; index++)
                            {
                                float reference = input.ReadSingle(), actual = spectrum[channel * bins + index];
                                if (!float.IsFinite(actual) || !float.IsFinite(reference))
                                {
                                    throw new InvalidDataException($"Vorbis spectrum {stream}/{frame}, channel {channel}, bin {index}: {actual:R}/{reference:R} is not finite.");
                                }
                                double delta = actual - reference;
                                energy += (double)reference * reference; error += delta * delta;
                                peak = Math.Max(peak, Math.Abs(reference)); difference = Math.Max(difference, Math.Abs(delta));
                            }
                        }
                        if (error > 1e-10 * Math.Max(energy, 1e-20) || difference > 3e-6 * Math.Max(1, peak))
                        {
                            throw new InvalidDataException($"Vorbis spectrum {stream}/{frame}: relative error {Math.Sqrt(error / Math.Max(energy, 1e-20)):R}; peak {difference:R}/{peak:R}.");
                        }
                        int samples = input.ReadInt32();
                        for (int sample = 0; sample < samples * channels; sample++) { input.ReadSingle(); }
                        total++;
                    }
                }
                if (allocator.AllocatedBytes != 0) { throw new InvalidDataException("Vorbis spectrum storage was not released."); }
            }
            if (input.BaseStream.ReadByte() != -1) { throw new InvalidDataException("The Vorbis spectrum reference has trailing data."); }
            return total;
        }

        private static void Repeat(byte[] packet, VorbisSetupCapture setup, int bins, Span<float> spectrum,
            Span<float> floor, Span<float> vector, Span<byte> classes)
        {
            VorbisBitReader reader = new(packet); reader.Read(1);
            int index = (int)reader.Read(VorbisBitReader.ILog((uint)setup.Modes.Count - 1));
            VorbisMode mode = setup.Modes[index];
            if (mode.LongBlock) { reader.Read(2); }
            VorbisSpectrum.Decode(ref reader, setup, mode, bins, spectrum, floor, vector, classes, default);
        }
    }
}
