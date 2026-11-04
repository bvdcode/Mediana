// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Text;
using Mediana.Codecs.H264;
using Mediana.Core.Memory;
using Mediana.Formats.Video;

namespace Mediana.Tests
{
    internal static class H264VlcWritingTestInput
    {
        public static void Verify()
        {
            string[] codes = new string[33];
            int[] sparse = new int[codes.Length];
            for (int index = 0; index < codes.Length - 1; index++)
            {
                codes[index] = new string('0', index) + "1";
            }
            codes[^1] = new string('0', 32);
            for (int index = 0; index < sparse.Length; index++)
            {
                sparse[index] = 3 * index + 2;
            }
            foreach (int[]? symbols in new int[]?[] { null, sparse })
            {
                H264Vlc table = new(codes, symbols);
                for (int alignment = 0; alignment < 8; alignment++)
                {
                    VerifySequence(table, codes, symbols, alignment, false);
                    VerifySequence(table, codes, symbols, alignment, true);
                }
            }
            string[] wideCodes = ["0", new string('1', 64)];
            int[] wideSymbols = [3, 65];
            H264Vlc wide = new(wideCodes, wideSymbols);
            for (int alignment = 0; alignment < 8; alignment++)
            {
                VerifySequence(wide, wideCodes, wideSymbols, alignment, false);
                VerifySequence(wide, wideCodes, wideSymbols, alignment, true);
            }
            VerifyAllocation(new(["1", "0"]));
        }

        private static void VerifySequence(H264Vlc table, string[] codes, int[]? symbols, int alignment, bool prefixed)
        {
            MediaBufferAllocator allocator = new(65536);
            using H264BitWriter writer = new(allocator, 32768, 0x65, prefixed);
            writer.WriteBits(0, alignment);
            StringBuilder expected = new(new string('0', alignment));
            for (int index = 0; index < 2048; index++)
            {
                int code = index * 17 % codes.Length;
                table.Write(writer, symbols?[code] ?? code);
                expected.Append(codes[code]);
                Require(writer.BitPosition == expected.Length);
            }
            foreach (int symbol in new[] { int.MinValue, -1, int.MaxValue, symbols is null ? codes.Length : 0 })
            {
                long position = writer.BitPosition;
                bool rejected = false;
                try
                {
                    table.Write(writer, symbol);
                }
                catch (ArgumentOutOfRangeException)
                {
                    rejected = true;
                }
                Require(rejected && writer.BitPosition == position);
            }
            writer.WriteBits(181, 8);
            expected.Append("10110101");
            byte[] payload = H264TestBits.Rbsp(expected.ToString());
            byte[] reference = new byte[payload.Length + (prefixed ? 5 : 1)];
            int prefix = prefixed ? 4 : 0;
            reference[prefix] = 0x65;
            payload.CopyTo(reference, prefix + 1);
            if (prefixed)
            {
                BinaryPrimitives.WriteUInt32BigEndian(reference, (uint)(payload.Length + 1));
            }
            using MediaBuffer packet = writer.Finish();
            Require(packet.Memory.Span.SequenceEqual(reference));
            packet.Dispose();
            writer.Dispose();
            Require(allocator.AllocatedBytes == 0);
        }

        private static void VerifyAllocation(H264Vlc table)
        {
            MediaBufferAllocator allocator = new(8192);
            using H264BitWriter writer = new(allocator, 4096, 0x65);
            table.Write(writer, 0);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < 16384; index++)
            {
                table.Write(writer, 0);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            if (allocated != 0 || writer.BitPosition != 16385)
            {
                throw new InvalidDataException($"H.264 VLC writing allocated {allocated} bytes at bit position {writer.BitPosition}; expected zero bytes and position 16385.");
            }
            writer.Dispose();
            Require(allocator.AllocatedBytes == 0);
        }

        private static void Require(bool condition)
        {
            if (!condition)
            {
                throw new InvalidDataException("H.264 VLC writing changed code bits, positions, rejection or ownership.");
            }
        }
    }
}
