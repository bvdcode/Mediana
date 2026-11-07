// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Opus;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    internal static class OpusImdctTestInput
    {
        public static int Verify(string root)
        {
            MediaBufferAllocator allocator = new(32 * 1024);
            using CeltTransformPlan plan = CeltTransformPlan.Create(allocator);
            using BinaryReader reader = new(File.OpenRead(Path.Combine(root, "inverse-mdct.bin")));
            if (!reader.ReadBytes(8).AsSpan().SequenceEqual("OPUSMDC1"u8))
            {
                throw new InvalidDataException("The CELT inverse-transform reference header is invalid.");
            }
            Span<float> spectrum = stackalloc float[7680], output = stackalloc float[1020], workspace = stackalloc float[960];
            int cases = reader.ReadInt32();
            for (int index = 0; index < cases; index++)
            {
                int shift = reader.ReadInt32(), stride = reader.ReadInt32();
                int size = 120 << shift;
                spectrum.Fill(float.NaN); output.Fill(float.NaN); workspace.Fill(float.NaN);
                float peakInput = 1;
                for (int at = 0; at < size; at++)
                {
                    float value = reader.ReadSingle();
                    spectrum[at * stride] = value;
                    peakInput = Math.Max(peakInput, Math.Abs(value));
                }
                for (int at = 0; at < 60; at++) { output[at] = reader.ReadSingle(); }
                CeltImdct.Transform(plan, spectrum, output, workspace, shift, stride);
                double errorEnergy = 0, referenceEnergy = 0;
                for (int at = 0; at < size + 60; at++)
                {
                    float expected = reader.ReadSingle();
                    double difference = (double)output[at] - expected;
                    if (!float.IsFinite(output[at]) || Math.Abs(difference) > peakInput * .0001)
                    {
                        throw new InvalidDataException($"CELT inverse-transform case {index}, sample {at}, differs: {output[at]} / {expected}.");
                    }
                    errorEnergy += difference * difference;
                    referenceEnergy += (double)expected * expected;
                }
                if (errorEnergy > referenceEnergy * 4e-12)
                {
                    throw new InvalidDataException($"CELT inverse-transform case {index} exceeds its relative error bound.");
                }
                for (int at = 0; at < size * stride; at++)
                {
                    if (at % stride != 0 && !float.IsNaN(spectrum[at]))
                    {
                        throw new InvalidDataException("The inverse transform changed unused interleaved coefficients.");
                    }
                }
            }
            if (reader.BaseStream.Position != reader.BaseStream.Length)
            {
                throw new InvalidDataException("The CELT inverse-transform reference has trailing bytes.");
            }
            return cases;
        }
    }
}
