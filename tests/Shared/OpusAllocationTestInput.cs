// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Opus;

namespace Mediana.Tests
{
    internal static class OpusAllocationTestInput
    {
        public static int Verify(string root)
        {
            using BinaryReader reader = new(File.OpenRead(Path.Combine(root, "band-allocation.bin")));
            if (!reader.ReadBytes(8).AsSpan().SequenceEqual("OPUSALC1"u8))
            {
                throw new InvalidDataException("The band allocation reference header is invalid.");
            }
            int cases = reader.ReadInt32();
            Span<int> shape = stackalloc int[21], fine = stackalloc int[21], boosts = stackalloc int[21];
            Span<byte> priorities = stackalloc byte[21];
            for (int index = 0; index < cases; index++)
            {
                byte[] data = reader.ReadBytes(reader.ReadInt32());
                OpusRangeDecoder decoder = new(data);
                int channels = reader.ReadInt32(), shift = reader.ReadInt32(), first = reader.ReadInt32(), end = reader.ReadInt32();
                CeltBandMode mode = new(channels, shift, first, end);
                int available = reader.ReadInt32(), trim = reader.ReadInt32();
                for (int band = 0; band < 21; band++) { boosts[band] = reader.ReadInt32(); }
                shape.Clear(); fine.Clear(); priorities.Clear();
                CeltAllocationInfo actual = CeltAllocationReader.Read(ref decoder, mode, boosts, available, trim,
                    new(shape, fine, priorities));
                CeltAllocationInfo expected = new(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32() != 0, reader.ReadInt32());
                if (actual != expected || decoder.Range != reader.ReadUInt32() || decoder.BitsUsed != reader.ReadInt32() ||
                    decoder.EighthBitsUsed != reader.ReadInt32())
                {
                    throw new InvalidDataException($"Band allocation case {index} has a different budget or stereo state: {actual} / {expected}.");
                }
                for (int band = 0; band < 21; band++)
                {
                    int expectedShape = reader.ReadInt32(), expectedFine = reader.ReadInt32(), expectedPriority = reader.ReadInt32();
                    if (shape[band] != expectedShape || fine[band] != expectedFine || priorities[band] != expectedPriority)
                    {
                        throw new InvalidDataException($"Band allocation case {index}, band {band}, differs: {shape[band]}/{fine[band]}/{priorities[band]} / {expectedShape}/{expectedFine}/{expectedPriority}.");
                    }
                }
            }
            if (reader.BaseStream.Position != reader.BaseStream.Length)
            {
                throw new InvalidDataException("The band allocation reference has trailing bytes.");
            }
            return cases;
        }
    }
}
