// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Formats.Video;

namespace Mediana.Tests
{
    public class VpxPresentationSyntaxTests
    {
        [Fact]
        public void Vp9ProfilesAndShowExistingAreClassifiedFromTheirBoundedUncompressedPrefix()
        {
            for (int profile = 0; profile < 4; profile++)
            {
                int baseValue = 0x80 | (profile & 1) << 5 | (profile & 2) << 3;
                int existingShift = profile == 3 ? 2 : 3;
                for (int shown = 0; shown < 2; shown++)
                {
                    Vp9FramePrefix prefix = Vp9FramePrefix.Read([(byte)(baseValue | shown << (existingShift - 2))]);
                    Assert.Equal(profile, prefix.Profile);
                    Assert.False(prefix.ShowExisting);
                    Assert.Equal(shown != 0, prefix.ShowFrame);
                }
                Vp9FramePrefix existing = Vp9FramePrefix.Read([(byte)(baseValue | 1 << existingShift)]);
                Assert.True(existing.ShowExisting);
                Assert.True(existing.ShowFrame);
            }
            Assert.Throws<InvalidDataException>(() => Vp9FramePrefix.Read([]));
            Assert.Throws<InvalidDataException>(() => Vp9FramePrefix.Read([0]));
            Assert.Throws<InvalidDataException>(() => Vp9FramePrefix.Read([0xb8]));
        }

        [Fact]
        public void Vp8TagsRejectReservedVersionsAndTruncatedPartitionExtents()
        {
            Vp8FrameTag visible = Vp8FrameTag.Read([0x50, 0, 0], 12);
            Assert.True(visible.KeyFrame);
            Assert.True(visible.ShowFrame);
            Assert.Equal(2, visible.ControlLength);
            Assert.False(Vp8FrameTag.Read([0x41, 0, 0], 5).ShowFrame);
            Assert.Throws<InvalidDataException>(() => Vp8FrameTag.Read([0x50, 0], 12));
            Assert.Throws<InvalidDataException>(() => Vp8FrameTag.Read([0x50, 0, 0], 11));
            Assert.Throws<NotSupportedException>(() => Vp8FrameTag.Read([0x58, 0, 0], 12));
        }
    }
}
