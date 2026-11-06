// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Opus;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    internal static class OpusSpectrumTestInput
    {
        public static int Verify(string root)
        {
            MediaBufferAllocator allocator = new(128 * 1024);
            using CeltPulseCodebook codebook = CeltPulseCodebook.Create(176, 128, allocator);
            using BinaryReader reader = new(File.OpenRead(Path.Combine(root, "spectral-frames.bin")));
            if (!reader.ReadBytes(8).AsSpan().SequenceEqual("OPUSSPC1"u8))
            {
                throw new InvalidDataException("The CELT spectral-frame reference header is invalid.");
            }
            Span<float> energies = stackalloc float[42], coefficients = stackalloc float[1920], folding = stackalloc float[1248];
            Span<int> tf = stackalloc int[21], boosts = stackalloc int[21], shape = stackalloc int[21], fine = stackalloc int[21];
            Span<byte> priority = stackalloc byte[21], masks = stackalloc byte[42];
            int cases = reader.ReadInt32();
            for (int index = 0; index < cases; index++)
            {
                byte[] bytes = reader.ReadBytes(reader.ReadInt32());
                OpusRangeDecoder decoder = new(bytes);
                CeltBandMode mode = new(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
                uint seed = reader.ReadUInt32();
                bool disableInversion = reader.ReadInt32() != 0;
                for (int at = 0; at < mode.Channels * 21; at++) { energies[at] = reader.ReadSingle(); }
                tf.Clear(); boosts.Clear(); shape.Clear(); fine.Clear(); priority.Clear();
                coefficients.Fill(float.NaN); folding.Fill(float.NaN); masks.Fill(255);
                CeltFrameSyntax syntax = CeltFrameSyntaxReader.Read(ref decoder, mode, energies, tf, boosts, new(shape, fine, priority));
                CeltSpectralFrameReader.Read(ref decoder, codebook, mode, syntax, tf, shape, new(coefficients, folding, masks), ref seed, disableInversion);
                if (seed != reader.ReadUInt32() || decoder.Range != reader.ReadUInt32() || decoder.BitsUsed != reader.ReadInt32() ||
                    decoder.EighthBitsUsed != reader.ReadInt32())
                {
                    throw new InvalidDataException($"CELT spectral frame {index} has a different noise seed or entropy state.");
                }
                for (int at = 0; at < mode.Channels * 21; at++)
                {
                    if (masks[at] != reader.ReadByte())
                    {
                        throw new InvalidDataException($"CELT spectral frame {index}, collapse mask {at}, differs.");
                    }
                }
                int samples = mode.Channels * (120 << mode.FrameShift);
                for (int at = 0; at < samples; at++)
                {
                    float expected = reader.ReadSingle();
                    if (!float.IsFinite(coefficients[at]) || Math.Abs(coefficients[at] - expected) > .000001f)
                    {
                        throw new InvalidDataException($"CELT spectral frame {index}, coefficient {at}, differs: {coefficients[at]} / {expected}.");
                    }
                }
            }
            if (reader.BaseStream.Position != reader.BaseStream.Length)
            {
                throw new InvalidDataException("The CELT spectral-frame reference has trailing bytes.");
            }
            return cases;
        }
    }
}
