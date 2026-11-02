// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Core.Memory;
using Mediana.Formats.Video;

namespace Mediana.Tests
{
    internal static class H264BitWritingTestInput
    {
        public static void Verify()
        {
            for (int alignment = 0; alignment < 8; alignment++)
            {
                for (int count = 0; count <= 64; count++)
                {
                    foreach (ulong pattern in new[] { 0UL, 1UL, ulong.MaxValue, 0xAAAAAAAAAAAAAAAAUL, 0x0123456789ABCDEFUL })
                    {
                        ulong value = count switch { 0 => 0, 64 => pattern, _ => pattern & ((1UL << count) - 1) };
                        Check([(0, alignment), (value, count), (0, 24), (3, 8)], alignment % 2 == 0, false);
                    }
                }
            }
            List<(ulong Value, int Count)> writes = [];
            Random random = new(763921);
            for (int index = 0; index < 2048; index++)
            {
                int count = random.Next(65);
                ulong value = (ulong)random.NextInt64() | ((ulong)random.Next(2) << 63);
                value = count switch { 0 => 0, 64 => value, _ => value & ((1UL << count) - 1) };
                writes.Add((value, count));
            }
            Check(writes, true, false);
            Check(writes, false, false);
            for (int value = 0; value <= byte.MaxValue; value++)
            {
                Check([(0, 19), ((ulong)value, 8), (0, 64), (1, 1), (ulong.MaxValue, 64)], true, true);
            }
            VerifyOverflow();
        }

        private static void Check(IReadOnlyList<(ulong Value, int Count)> writes, bool prefixed, bool restore)
        {
            MediaBufferAllocator allocator = new(65536);
            using H264BitWriter writer = new(allocator, 32768, 0x65, prefixed, measureOverflow: true);
            List<bool> expected = [];
            for (int index = 0; index < writes.Count; index++)
            {
                (ulong value, int count) = writes[index];
                writer.WriteBits(value, count);
                AddBits(expected, value, count);
                if (writer.BitPosition != expected.Count)
                {
                    throw new InvalidDataException("H.264 writing changed the RBSP position across a partial byte.");
                }
                if (restore && index == 0)
                {
                    H264BitWriterCheckpoint checkpoint = writer.CreateCheckpoint();
                    for (int block = 0; block < 4096; block++)
                    {
                        writer.WriteBits(0, 64);
                    }
                    if (writer.EncodedLength <= 32768)
                    {
                        throw new InvalidDataException("The checkpoint case did not exceed the storage budget.");
                    }
                    writer.Restore(checkpoint);
                    if (writer.BitPosition != expected.Count)
                    {
                        throw new InvalidDataException("Restoring overflow lost the partial byte position.");
                    }
                }
            }
            using MediaBuffer packet = writer.Finish();
            byte[] reference = Reference(expected, prefixed);
            if (!packet.Memory.Span.SequenceEqual(reference) || writer.EncodedLength != reference.Length)
            {
                throw new InvalidDataException("H.264 NAL bytes differ from independently packed and escaped bits.");
            }
            packet.Dispose();
            writer.Dispose();
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidDataException("H.264 bit writing retained packet storage.");
            }
        }

        private static void VerifyOverflow()
        {
            MediaBufferAllocator allocator = new(4096);
            using H264BitWriter writer = new(allocator, 8, 0x65, true, measureOverflow: true);
            List<bool> expected = [];
            for (int index = 0; index < 128; index++)
            {
                writer.WriteBits(0, 63);
                AddBits(expected, 0, 63);
            }
            if (writer.TryFinish() is not null || writer.EncodedLength != Reference(expected, true).Length || allocator.AllocatedBytes != 8)
            {
                throw new InvalidDataException("H.264 overflow measurement changed escaped length or storage capacity.");
            }
        }

        private static void AddBits(List<bool> destination, ulong value, int count)
        {
            for (int bit = count - 1; bit >= 0; bit--)
            {
                destination.Add(((value >> bit) & 1) != 0);
            }
        }

        private static byte[] Reference(List<bool> bits, bool prefixed)
        {
            int payloadLength = (bits.Count + 8) / 8;
            byte[] rbsp = new byte[payloadLength];
            for (int index = 0; index <= bits.Count; index++)
            {
                if (index == bits.Count || bits[index])
                {
                    rbsp[index / 8] |= (byte)(128 >> (index % 8));
                }
            }
            List<byte> escaped = [0x65];
            foreach (byte value in rbsp)
            {
                if (value <= 3 && escaped.Count >= 3 && escaped[^1] == 0 && escaped[^2] == 0)
                {
                    escaped.Add(3);
                }
                escaped.Add(value);
            }
            byte[] result = new byte[escaped.Count + (prefixed ? 4 : 0)];
            escaped.CopyTo(result, prefixed ? 4 : 0);
            if (prefixed)
            {
                BinaryPrimitives.WriteUInt32BigEndian(result, (uint)escaped.Count);
            }
            return result;
        }
    }
}
