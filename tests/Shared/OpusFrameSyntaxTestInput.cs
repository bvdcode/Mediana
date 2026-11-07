// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Opus;

namespace Mediana.Tests
{
    internal static class OpusFrameSyntaxTestInput
    {
        public static int Verify(string root)
        {
            using BinaryReader reader = new(File.OpenRead(Path.Combine(root, "frame-syntax.bin")));
            if (!reader.ReadBytes(8).AsSpan().SequenceEqual("OPUSCEL1"u8))
            {
                throw new InvalidDataException("The frame syntax reference header is invalid.");
            }
            int cases = reader.ReadInt32();
            Span<float> energies = stackalloc float[42];
            Span<int> tf = stackalloc int[21], boost = stackalloc int[21], shape = stackalloc int[21], fine = stackalloc int[21];
            Span<byte> priority = stackalloc byte[21];
            for (int index = 0; index < cases; index++)
            {
                byte[] bytes = reader.ReadBytes(reader.ReadInt32());
                OpusRangeDecoder decoder = new(bytes);
                CeltBandMode mode = new(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
                Span<float> active = energies[..(21 * mode.Channels)];
                for (int at = 0; at < active.Length; at++) { active[at] = reader.ReadSingle(); }
                tf.Clear(); boost.Clear(); shape.Clear(); fine.Clear(); priority.Clear();
                CeltFrameSyntax actual = CeltFrameSyntaxReader.Read(ref decoder, mode, active, tf, boost, new(shape, fine, priority));
                CeltFrameFlags flags = new(reader.ReadInt32() != 0, reader.ReadInt32() != 0, reader.ReadInt32() != 0,
                    new(reader.ReadInt32(), reader.ReadSingle(), reader.ReadInt32()));
                CeltAllocationControls controls = new((CeltSpreading)reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
                CeltAllocationInfo allocation = new(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32() != 0, reader.ReadInt32());
                CeltFrameSyntax expected = new(flags, controls, allocation);
                if (actual != expected || decoder.Range != reader.ReadUInt32() || decoder.BitsUsed != reader.ReadInt32() ||
                    decoder.EighthBitsUsed != reader.ReadInt32())
                {
                    throw new InvalidDataException($"Frame syntax case {index} has different flags, allocation or entropy: {actual} / {expected}.");
                }
                for (int at = 0; at < active.Length; at++)
                {
                    if (BitConverter.SingleToInt32Bits(active[at]) != reader.ReadInt32())
                    {
                        throw new InvalidDataException($"Frame syntax case {index}, energy {at}, differs.");
                    }
                }
                for (int band = 0; band < 21; band++)
                {
                    int expectedTf = reader.ReadInt32(), expectedBoost = reader.ReadInt32(), expectedShape = reader.ReadInt32();
                    int expectedFine = reader.ReadInt32(), expectedPriority = reader.ReadInt32();
                    if (tf[band] != expectedTf || boost[band] != expectedBoost || shape[band] != expectedShape ||
                        fine[band] != expectedFine || priority[band] != expectedPriority)
                    {
                        throw new InvalidDataException($"Frame syntax case {index}, band {band}, differs.");
                    }
                }
            }
            if (reader.BaseStream.Position != reader.BaseStream.Length)
            {
                throw new InvalidDataException("The frame syntax reference has trailing bytes.");
            }
            return cases;
        }
    }
}
