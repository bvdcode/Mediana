// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Core.Memory;
using Mediana.Formats.Video;

namespace Mediana.Tests
{
    public class H264ActiveReferenceTests
    {
        [Theory]
        [InlineData((int)H264PictureStructure.Frame, (int)H264SliceKind.P, 16)]
        [InlineData((int)H264PictureStructure.Frame, (int)H264SliceKind.B, 16)]
        [InlineData((int)H264PictureStructure.TopField, (int)H264SliceKind.P, 32)]
        [InlineData((int)H264PictureStructure.BottomField, (int)H264SliceKind.B, 32)]
        public void HeaderAppliesThePictureLimitToBothExplicitAndInheritedCounts(int structureValue, int kindValue, int maximum)
        {
            H264PictureStructure structure = (H264PictureStructure)structureValue;
            H264SliceKind kind = (H264SliceKind)kindValue;
            foreach (bool overridden in new[] { false, true })
            {
                foreach (int count in new[] { 1, maximum })
                {
                    H264SliceHeader header = Read(structure, kind, count, count, overridden);
                    Assert.Equal(count, header.ActiveReferencesL0);
                    int expectedL1 = kind switch
                    {
                        H264SliceKind.P => 0,
                        H264SliceKind.B => count,
                        _ => throw new ArgumentOutOfRangeException(nameof(kindValue))
                    };
                    Assert.Equal(expectedL1, header.ActiveReferencesL1);
                }
                Assert.Throws<InvalidDataException>(() => Read(structure, kind, maximum + 1, 1, overridden));
                if (kind == H264SliceKind.B)
                {
                    Assert.Throws<InvalidDataException>(() => Read(structure, kind, 1, maximum + 1, overridden));
                }
            }
        }

        [Fact]
        public void AdaptiveFramesUseTheFrameLimitBeforeMacroblockFieldExpansion()
        {
            H264SliceHeader header = Read(H264PictureStructure.Frame, H264SliceKind.B, 16, 16, true, adaptive: true);
            Assert.True(header.Layout.MacroblockAdaptiveFrameField);
            Assert.Equal(16, header.ActiveReferencesL0);
            Assert.Throws<InvalidDataException>(() => Read(H264PictureStructure.Frame, H264SliceKind.B, 17, 1, true, adaptive: true));
        }

        private static H264SliceHeader Read(H264PictureStructure structure, H264SliceKind kind,
            int l0, int l1, bool overridden, bool adaptive = false)
        {
            H264SequenceParameterSet sequence = new()
            {
                FrameNumberBits = 4,
                PictureOrderType = 0,
                PictureOrderBits = 4,
                WidthInMbs = 2,
                HeightInMapUnits = 2,
                MbAdaptiveFrameField = adaptive
            };
            H264PictureParameterSet picture = new(0, 0, false, false, 26, 0, 0, false, false, false, false,
                overridden ? 32 : l0, overridden ? 32 : l1, false, 0);
            H264Configuration configuration = new(4, new Dictionary<int, H264SequenceParameterSet> { [0] = sequence },
                new Dictionary<int, H264PictureParameterSet> { [0] = picture });
            MediaBufferAllocator allocator = new(4096);
            using H264BitWriter writer = new(allocator, 512, 0x41);
            writer.WriteUnsigned(0);
            writer.WriteUnsigned(kind switch
            {
                H264SliceKind.P => 0,
                H264SliceKind.B => 1,
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            });
            writer.WriteUnsigned(0);
            writer.WriteBits(1, 4);
            writer.WriteFlag(structure != H264PictureStructure.Frame);
            if (structure != H264PictureStructure.Frame)
            {
                writer.WriteFlag(structure == H264PictureStructure.BottomField);
            }
            writer.WriteBits(2, 4);
            if (kind == H264SliceKind.B)
            {
                writer.WriteFlag(true);
            }
            writer.WriteFlag(overridden);
            if (overridden)
            {
                writer.WriteUnsigned((uint)(l0 - 1));
                if (kind == H264SliceKind.B)
                {
                    writer.WriteUnsigned((uint)(l1 - 1));
                }
            }
            writer.WriteFlag(false);
            if (kind == H264SliceKind.B)
            {
                writer.WriteFlag(false);
            }
            writer.WriteFlag(false);
            writer.WriteSigned(0);
            using MediaBuffer nal = writer.Finish();
            H264BitReader bits = new(nal.Memory.Span[1..]);
            return H264SliceHeaderReader.Read(ref bits, configuration, 0x41);
        }
    }
}
