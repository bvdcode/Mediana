// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Core.Memory;
using Mediana.Formats.Video;

namespace Mediana.Tests
{
    internal static class H264CavlcLevelTestInput
    {
        public static void Verify()
        {
            for (int suffix = 0; suffix <= 6; suffix++)
            {
                for (int alignment = 0; alignment < 8; alignment++)
                {
                    VerifySequence(suffix, alignment);
                }
            }
        }

        private static void VerifySequence(int suffix, int alignment)
        {
            MediaBufferAllocator allocator = new(131072);
            using H264BitWriter writer = new(allocator, 65536, 0x41);
            writer.WriteBits((1UL << alignment) - 1, alignment);
            int accepted = 0;
            long position = alignment;
            for (int code = 0; code <= ushort.MaxValue; code++)
            {
                (bool valid, uint _, int length) = Enumerate(code, suffix);
                if (H264CavlcWriter.TryWriteLevel(writer, code, suffix) != valid)
                {
                    throw new InvalidDataException("CAVLC level coding changed its accepted range.");
                }
                position += length;
                if (writer.BitPosition != position)
                {
                    throw new InvalidDataException("CAVLC level coding changed bit consumption or wrote a rejected code.");
                }
                if (valid)
                {
                    accepted++;
                }
            }
            int expectedCount = suffix switch { 0 or 1 => 4126, 2 => 4156, 3 => 4216, 4 => 4336, 5 => 4576, 6 => 5056, _ => throw new ArgumentOutOfRangeException(nameof(suffix)) };
            if (accepted != expectedCount)
            {
                throw new InvalidDataException("The level range does not cover the baseline prefix and escape regions.");
            }
            using MediaBuffer packet = writer.Finish();
            H264BitReader reader = new(packet.Memory.Span[1..]);
            if (reader.ReadBits(alignment) != (1u << alignment) - 1)
            {
                throw new InvalidDataException("CAVLC level coding changed preceding bits.");
            }
            for (int code = 0; code <= ushort.MaxValue; code++)
            {
                (bool valid, uint value, int length) = Enumerate(code, suffix);
                if (valid && reader.ReadBits(length) != value)
                {
                    throw new InvalidDataException("CAVLC level bits differ from independently enumerated prefix and suffix fields.");
                }
            }
            if (reader.BitPosition != position)
            {
                throw new InvalidDataException("CAVLC level coding changed the complete sequence length.");
            }
            reader.ReadTrailingBits();
            packet.Dispose();
            writer.Dispose();
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidOperationException("CAVLC level verification retained media storage.");
            }
        }

        private static (bool Valid, uint Value, int Length) Enumerate(int code, int suffixLength)
        {
            for (int prefix = 0; prefix <= 15; prefix++)
            {
                int width = prefix switch { 14 when suffixLength == 0 => 4, 15 => 12, _ => suffixLength };
                int first = Math.Min(prefix, 15) * (1 << suffixLength);
                if (prefix == 15 && suffixLength == 0)
                {
                    first += 15;
                }
                int last = first + (1 << width) - 1;
                if (code >= first && code <= last)
                {
                    return (true, (uint)((1 << width) + code - first), prefix + 1 + width);
                }
            }
            return (false, 0, 0);
        }
    }
}
