// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Vp9;

namespace Mediana.Codecs.Vp9.Tests
{
    public class Vp9SuperframeTests
    {
        [Fact]
        public void AllSizeWidthsAndFrameCountsProduceExactBorrowedRanges()
        {
            for (int bytes = 1; bytes <= 4; bytes++)
            {
                for (int count = 1; count <= 8; count++)
                {
                    byte marker = (byte)(0xc0 | ((bytes - 1) << 3) | (count - 1));
                    List<byte> packet = [];
                    for (int i = 0; i < count; i++) { packet.AddRange(Enumerable.Repeat((byte)i, i + 1)); }
                    packet.Add(marker);
                    for (int i = 0; i < count; i++)
                    { for (int b = 0; b < bytes; b++) { packet.Add((byte)((i + 1) >> (b * 8))); } }
                    packet.Add(marker);
                    Range[] ranges = new Range[8]; byte[] encoded = packet.ToArray();
                    Assert.Equal(count, Vp9Superframe.Parse(encoded, ranges));
                    for (int i = 0; i < count; i++) { Assert.Equal(Enumerable.Repeat((byte)i, i + 1), encoded[ranges[i]]); }
                }
            }
        }

        [Fact]
        public void AnnexBRequiresMatchingMarkersBeforeInterpretingAnIndex()
        {
            Range[] frames = new Range[8];
            Assert.Equal(1, Vp9Superframe.Parse([0x82, 0x01], frames)); Assert.Equal(0..2, frames[0]);
            Assert.Equal(1, Vp9Superframe.Parse([0x82, 0xc7], frames)); Assert.Equal(0..2, frames[0]);
            Assert.Equal(1, Vp9Superframe.Parse([0, 1, 0xc0], frames)); Assert.Equal(0..3, frames[0]);
            Assert.Throws<InvalidDataException>(() => Vp9Superframe.Parse([], frames));
            Assert.Throws<InvalidDataException>(() => Vp9Superframe.Parse([0x82, 0xc0, 0, 0xc0], frames));
            Assert.Throws<InvalidDataException>(() => Vp9Superframe.Parse([0x82, 0xc0, 2, 0xc0], frames));
            Assert.Throws<InvalidDataException>(() => Vp9Superframe.Parse([0x82, 0x01, 0xc0, 1, 0xc0], frames));
        }
    }
}
