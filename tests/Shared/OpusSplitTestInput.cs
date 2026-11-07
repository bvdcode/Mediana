// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Opus;

namespace Mediana.Tests
{
    internal static class OpusSplitTestInput
    {
        public static int Verify(string root)
        {
            using BinaryReader reader = new(File.OpenRead(Path.Combine(root, "split-angles.bin")));
            if (!reader.ReadBytes(8).AsSpan().SequenceEqual("OPUSTHT1"u8))
            {
                throw new InvalidDataException("The CELT split-angle reference header is invalid.");
            }
            int approximations = reader.ReadInt32();
            for (int angle = 64; angle < 64 + approximations; angle++)
            {
                int cosine = CeltSplitMath.Cosine(angle);
                int tangent = CeltSplitMath.Log2Tangent(CeltSplitMath.Cosine(16384 - angle), cosine);
                if (cosine != reader.ReadInt32() || tangent != reader.ReadInt32())
                {
                    throw new InvalidDataException($"The CELT split approximation differs at angle {angle}.");
                }
            }
            int cases = reader.ReadInt32();
            for (int index = 0; index < cases; index++)
            {
                byte[] bytes = reader.ReadBytes(reader.ReadInt32());
                OpusRangeDecoder decoder = new(bytes);
                CeltSplitMode mode = new(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(),
                    reader.ReadInt32(), reader.ReadInt32() != 0, reader.ReadInt32(), reader.ReadInt32() != 0);
                int budget = reader.ReadInt32(), remaining = reader.ReadInt32();
                CeltSplitAngle actual = CeltSplitAngleReader.Read(ref decoder, mode, budget, remaining, reader.ReadUInt32());
                CeltSplitAngle expected = new(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(),
                    reader.ReadInt32(), reader.ReadInt32() != 0, reader.ReadUInt32());
                if (actual != expected || decoder.Range != reader.ReadUInt32() || decoder.BitsUsed != reader.ReadInt32() ||
                    decoder.EighthBitsUsed != reader.ReadInt32())
                {
                    throw new InvalidDataException($"CELT split case {index} differs: {actual} / {expected}.");
                }
            }
            if (reader.BaseStream.Position != reader.BaseStream.Length)
            {
                throw new InvalidDataException("The CELT split-angle reference has trailing bytes.");
            }
            return cases;
        }
    }
}
