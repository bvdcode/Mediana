// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.IO.Compression;
using Mediana.Codecs.Vorbis;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    internal static class VorbisImdctTestInput
    {
        public static int Verify(string root, bool measureAllocations = false)
        {
            using FileStream file = File.OpenRead(Path.Combine(root, "inverse-mdct.bin.gz"));
            using GZipStream gzip = new(file, CompressionMode.Decompress);
            using BinaryReader input = new(gzip);
            if (!input.ReadBytes(8).AsSpan().SequenceEqual("VORMDCT1"u8)) { throw new InvalidDataException("The Vorbis transform reference header is invalid."); }
            int count = input.ReadInt32();
            MediaBufferAllocator allocator = new(1024 * 1024);
            for (int test = 0; test < count; test++)
            {
                int size = input.ReadInt32();
                float[] spectrum = new float[size / 2], output = new float[size], work = new float[size / 2];
                for (int index = 0; index < spectrum.Length; index++) { spectrum[index] = input.ReadSingle(); }
                using (VorbisTransformPlan plan = VorbisTransformPlan.Create(size, allocator))
                {
                    VorbisImdct.Transform(plan, spectrum, output, work);
                    double energy = 0, error = 0, peak = 0, difference = 0;
                    for (int index = 0; index < size; index++)
                    {
                        float reference = input.ReadSingle();
                        if (!float.IsFinite(output[index])) { throw new InvalidDataException("The Vorbis inverse transform is not finite."); }
                        double delta = output[index] - reference;
                        energy += (double)reference * reference; error += delta * delta;
                        peak = Math.Max(peak, Math.Abs(reference)); difference = Math.Max(difference, Math.Abs(delta));
                    }
                    if (error > 1e-10 * Math.Max(energy, 1e-30) || difference > 3e-6 * Math.Max(1, peak))
                    {
                        throw new InvalidDataException($"Vorbis transform {test}, size {size}: relative error {Math.Sqrt(error / Math.Max(energy, 1e-30)):R}; peak {difference:R}/{peak:R}.");
                    }
                    if (measureAllocations)
                    {
                        for (int pass = 0; pass < 4; pass++) { VorbisImdct.Transform(plan, spectrum, output, work); }
                        long before = GC.GetAllocatedBytesForCurrentThread(), reserved = allocator.AllocatedBytes;
                        for (int pass = 0; pass < 16; pass++) { VorbisImdct.Transform(plan, spectrum, output, work); }
                        if (GC.GetAllocatedBytesForCurrentThread() != before || allocator.AllocatedBytes != reserved)
                        {
                            throw new InvalidDataException("The Vorbis inverse transform allocated working storage.");
                        }
                    }
                }
                if (allocator.AllocatedBytes != 0) { throw new InvalidDataException("The Vorbis transform plan was not released."); }
            }
            if (input.BaseStream.ReadByte() != -1) { throw new InvalidDataException("The Vorbis transform reference has trailing data."); }
            return count;
        }
    }
}
