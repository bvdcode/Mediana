// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.IO.Compression;
using Mediana.Codecs.Hevc;

namespace Mediana.Tests
{
    internal static class HevcCabacTestInput
    {
        public static int Verify(string root, bool measureAllocations = false)
        {
            using GZipStream file = new(File.OpenRead(Path.Combine(root, "cabac.bin.gz")), CompressionMode.Decompress);
            using BinaryReader input = new(file);
            if (!input.ReadBytes(8).AsSpan().SequenceEqual("HEVCCAB1"u8)) { throw new InvalidDataException("Invalid HEVC entropy reference."); }
            int cases = input.ReadInt32(), total = 0;
            for (int test = 0; test < cases; test++)
            {
                int type = input.ReadInt32(), qp = input.ReadInt32(), groups = input.ReadInt32();
                ReadOnlySpan<int> offsets = HevcCabacInitialization.GetOffsets(type).Span;
                CabacContext[] contexts = new CabacContext[offsets[^1]];
                HevcCabacInitialization.Initialize(contexts, type, qp);
                if (groups != offsets.Length - 1) { throw new InvalidDataException("The HEVC context group count differs."); }
                for (int group = 0; group < groups; group++)
                {
                    int count = input.ReadInt32();
                    if (count != offsets[group + 1] - offsets[group]) { throw new InvalidDataException("The HEVC context group size differs."); }
                    for (int i = 0; i < count; i++)
                    {
                        int expected = input.ReadByte(); CabacContext actual = contexts[offsets[group] + i];
                        if (actual.State != expected >> 1 || actual.MostProbableSymbol != ((expected & 1) != 0))
                        {
                            throw new InvalidDataException($"HEVC context {type}/{qp}/{group}/{i} differs from the independent model.");
                        }
                    }
                }
                int checkpoint = input.ReadInt32();
                byte[] bytes = input.ReadBytes(input.ReadInt32());
                int count1 = input.ReadInt32(); byte[] operations = input.ReadBytes(checked(count1 * 3));
                HevcCabacReader reader = new(new(new ArraySegment<byte>(bytes)), qp, type);
                long before = GC.GetAllocatedBytesForCurrentThread();
                Decode(reader, operations.AsSpan(0, checkpoint * 3));
                VerifyAllocations(before, measureAllocations);
                reader.StoreWavefrontContexts();
                before = GC.GetAllocatedBytesForCurrentThread();
                Decode(reader, operations.AsSpan(checkpoint * 3));
                if (!reader.Termination()) { throw new InvalidDataException("Missing HEVC arithmetic termination."); }
                VerifyAllocations(before, measureAllocations);
                byte[] next = input.ReadBytes(input.ReadInt32());
                int count2 = input.ReadInt32(); operations = input.ReadBytes(checked(count2 * 3));
                HevcCabacReader copied = new(new(new ArraySegment<byte>(next)), qp, type);
                copied.CopyWavefrontContexts(reader);
                reader.StartWavefrontRow(new(new ArraySegment<byte>(next)), qp);
                before = GC.GetAllocatedBytesForCurrentThread();
                Decode(reader, operations); Decode(copied, operations);
                if (!reader.Termination() || !copied.Termination()) { throw new InvalidDataException("Missing HEVC WPP termination."); }
                VerifyAllocations(before, measureAllocations);
                total += count1 + count2;
            }
            if (file.ReadByte() != -1) { throw new InvalidDataException("The HEVC reference has trailing data."); }
            return total;
        }

        private static void Decode(HevcCabacReader reader, ReadOnlySpan<byte> operations)
        {
            for (int i = 0; i < operations.Length; i += 3)
            {
                int group = operations[i], index = operations[i + 1];
                bool expected = operations[i + 2] != 0;
                bool actual = group == 255 ? reader.Bypass() : reader.Read((CabacElement)group, index);
                if (actual != expected) { throw new InvalidDataException($"HEVC entropy bin {i / 3}, group {group}/{index} differs."); }
            }
        }

        private static void VerifyAllocations(long before, bool measure)
        {
            if (measure && GC.GetAllocatedBytesForCurrentThread() != before) { throw new InvalidDataException("HEVC entropy reconstruction allocated managed storage."); }
        }
    }
}
