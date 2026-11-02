// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Formats.Video;

namespace Mediana.Tests
{
    internal static class H264BitReadingTestInput
    {
        public static void Verify()
        {
            byte[] raw = new byte[256];
            new Random(82736).NextBytes(raw);
            for (int index = 0; index < 64; index++) { raw[index] = (byte)(index / 16); }
            byte[] encoded = H264TestBits.Escape(raw);
            for (int alignment = 0; alignment < 8; alignment++)
            {
                H264BitReader bits = new(encoded);
                bits.ReadBits(alignment);
                int position = alignment;
                int iteration = 0;
                while (position < raw.Length * 8)
                {
                    Require(bits.BitPosition == position && bits.ByteAligned == ((position & 7) == 0));
                    int count = Math.Min(iteration % 33, raw.Length * 8 - position);
                    H264BitReader copy = bits;
                    Require(copy.ReadBits(count) == ReadReference(raw, position, count));
                    if (iteration % 3 == 0)
                    {
                        Require(bits.ReadBit() == ReadReference(raw, position, 1));
                        count = 1;
                    }
                    else
                    {
                        Require(bits.ReadBits(count) == ReadReference(raw, position, count));
                    }
                    position += count;
                    Require(bits.BitPosition == position);
                    iteration++;
                }
                Require(bits.ReadBits(0) == 0 && !bits.MoreRbspData());
            }
            VerifyAlignment();
            VerifyPadding();
            foreach (byte[] invalid in new byte[][] { [0, 0, 0], [0, 0, 1], [0, 0, 2], [0, 0, 3], [0, 0, 3, 4] })
            {
                H264BitReader bits = new(invalid);
                bits.ReadBits(16);
                bool rejected = false;
                try { bits.ReadBit(); }
                catch (InvalidDataException) { rejected = true; }
                Require(rejected);
            }
        }

        private static void VerifyAlignment()
        {
            for (int alignment = 0; alignment < 8; alignment++)
            {
                H264BitReader ones = new([255, 101]);
                ones.ReadBits(alignment);
                ones.AlignOne();
                Require(ones.ByteAligned && ones.ReadBits(8) == (alignment == 0 ? 255u : 101u));
                H264BitReader zeros = new([0, 101]);
                zeros.ReadBits(alignment);
                zeros.AlignZero();
                Require(zeros.ByteAligned && zeros.ReadBits(8) == (alignment == 0 ? 0u : 101u));
            }
            H264BitReader trailing = new([128]);
            Require(trailing.ByteAligned && trailing.BitPosition == 0 && !trailing.MoreRbspData());
            trailing.ReadTrailingBits();
            Require(trailing.BitPosition == 8);
        }

        private static void VerifyPadding()
        {
            foreach (byte[] data in new byte[][] { [0, 0], [0, 0, 3] })
            {
                H264BitReader bits = new(data);
                bits.ReadCabacPadding();
                Require(bits.BitPosition == 16 && bits.ByteAligned && !bits.MoreRbspData());
            }
            H264BitReader terminal = new([159, 0, 0, 3]);
            terminal.ReadBits(3);
            terminal.ReadCabacPadding();
            Require(terminal.BitPosition == 24 && terminal.ByteAligned);
            H264BitReader truncated = new([0]);
            bool rejected = false;
            try { truncated.ReadCabacPadding(); }
            catch (InvalidDataException) { rejected = true; }
            Require(rejected);
        }

        private static uint ReadReference(ReadOnlySpan<byte> bytes, int position, int count)
        {
            uint value = 0;
            for (int index = 0; index < count; index++)
            {
                int bit = position + index;
                value = (value << 1) | (uint)((bytes[bit / 8] >> (7 - bit % 8)) & 1);
            }
            return value;
        }

        private static void Require(bool condition)
        {
            if (!condition) { throw new InvalidDataException("H.264 bit reading changed data, alignment, endings or checked bit position."); }
        }
    }
}
