// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using static Mediana.Tests.AvcFramingTestData;
using Mediana.Containers.Matroska;
using Mediana.Containers.MpegTs;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    public class AvcColorFramingTests
    {
        [Theory]
        [InlineData(1, false)]
        [InlineData(2, true)]
        [InlineData(4, false)]
        [InlineData(4, true)]
        public void ReplacesEveryKnownInBandSpsAndPreservesPpsSeiAndSlices(int lengthSize, bool idr)
        {
            byte[] sequence = H264MetadataTestInput.Sequence();
            byte[] picture = H264MetadataTestInput.Picture();
            byte[] configuration = H264MetadataTestInput.Configuration([sequence], [picture]);
            configuration[4] = (byte)(0xfc | lengthSize - 1);
            byte[] sei = [6, 0, 0, 3, 1, 0x80];
            byte[] slice = [idr ? (byte)0x65 : (byte)0x41, 0x9a];
            byte[] input = Packet(lengthSize, sequence, picture, sei, slice);
            MediaBufferAllocator allocator = new(8192);
            AvcAnnexBConverter converter = new(configuration, new CicpColorDescription(6, 14, 5, true), allocator, 512);
            Assert.Equal(0, allocator.AllocatedBytes);
            using (MediaBuffer result = converter.Convert(input, allocator, 4096, out bool actualIdr))
            {
                List<byte[]> nals = Nals(result.Memory.Span);
                Assert.Equal(idr, actualIdr);
                Assert.Equal(idr ? 7 : 5, nals.Count);
                foreach (byte[] nal in nals.Where(nal => (nal[0] & 31) == 7))
                {
                    H264VuiParameters vui = H264SequenceParser.Parse(nal).Vui!;
                    Assert.Equal(6, vui.ColorPrimaries);
                    Assert.Equal(14, vui.TransferCharacteristics);
                    Assert.Equal(5, vui.MatrixCoefficients);
                    Assert.True(vui.FullRange);
                }
                Assert.Equal(sei, nals[^2]);
                Assert.Equal(slice, nals[^1]);
                Assert.All(nals.Where(nal => (nal[0] & 31) == 8), nal => Assert.Equal(picture, nal));
                Assert.Throws<NotSupportedException>(() => converter.Convert(input, allocator, result.Memory.Length - 1, out _));
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void EachConfigurationSpsKeepsItsRangeWhenContainerRangeIsAbsent()
        {
            byte[] limited = H264MetadataTestInput.Sequence(0, vui: "00 1 101 0 1 00000001 00000001 00000001 000000");
            byte[] full = H264MetadataTestInput.Sequence(1, vui: "00 1 101 1 1 00000001 00000001 00000001 000000");
            byte[] configuration = H264MetadataTestInput.Configuration([limited, full], [H264MetadataTestInput.Picture()]);
            MediaBufferAllocator allocator = new(8192);
            AvcAnnexBConverter converter = new(configuration, new CicpColorDescription(2, 2, 2), allocator, 512);
            using (MediaBuffer result = converter.Convert(Packet(4, [0x65, 0x88]), allocator, 4096, out _))
            {
                H264VuiParameters[] colors = Nals(result.Memory.Span).Where(nal => (nal[0] & 31) == 7)
                    .Select(nal => H264SequenceParser.Parse(nal).Vui!).ToArray();
                Assert.Equal(2, colors.Length);
                Assert.False(colors[0].FullRange);
                Assert.True(colors[1].FullRange);
                Assert.All(colors, color => Assert.Equal((2, 2, 2), (color.ColorPrimaries, color.TransferCharacteristics, color.MatrixCoefficients)));
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void MatroskaColourRewritesOnlyDeclaredCicpComponentsAndChromaAxes(bool hasVui)
        {
            byte[] sequence = hasVui
                ? H264MetadataTestInput.Sequence(vui: "00 1 101 0 1 00000111 00001110 00000110 1 010 011 00000")
                : H264MetadataTestInput.Sequence();
            byte[] configuration = H264MetadataTestInput.Configuration([sequence], [H264MetadataTestInput.Picture()]);
            MatroskaColorDescription color = new(new() { MatrixCoefficients = 9, Range = 2, ChromaSitingHorz = 2 });
            MediaBufferAllocator allocator = new(8192);
            AvcAnnexBConverter converter = new(configuration, color, allocator);
            using MediaBuffer packet = converter.Convert(Packet(4, [0x65, 0x88]), allocator, 4096, out _);
            byte[] sps = Nals(packet.Memory.Span).Single(nal => (nal[0] & 31) == 7);
            H264VuiParameters vui = H264SequenceParser.Parse(sps).Vui!;
            Assert.Equal(hasVui ? 7 : 2, vui.ColorPrimaries);
            Assert.Equal(hasVui ? 14 : 2, vui.TransferCharacteristics);
            Assert.Equal(9, vui.MatrixCoefficients);
            Assert.True(vui.FullRange);
            Assert.Equal(1, vui.ChromaLocationTop);
            Assert.Equal(hasVui ? 3 : 1, vui.ChromaLocationBottom);
        }

        [Fact]
        public void RejectsChangingSpsAndReleasesConstructorBuffersOnInvalidConfiguration()
        {
            byte[] sequence = H264MetadataTestInput.Sequence();
            byte[] configuration = H264MetadataTestInput.Configuration([sequence], [H264MetadataTestInput.Picture()]);
            MediaBufferAllocator allocator = new(8192);
            CicpColorDescription color = new(1, 1, 1);
            AvcAnnexBConverter converter = new(configuration, color, allocator, 512);
            Assert.Throws<NotSupportedException>(() => converter.Convert(Packet(4, H264MetadataTestInput.Sequence(1), [0x65, 0x88]), allocator, 4096, out _));
            configuration = configuration[..^1];
            Assert.Throws<InvalidDataException>(() => new AvcAnnexBConverter(configuration, color, allocator, 512));
            Assert.Throws<ArgumentNullException>(() => new AvcAnnexBConverter(configuration, color));
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void UnrepresentableSourceDescriptionsFailBeforeDestinationIo(bool icc)
        {
            VideoColorDescription color;
            if (icc)
            {
                color = new IccColorDescription([1, 2, 3], false);
            }
            else
            {
                color = new CicpColorDescription(65535, 1, 1);
            }
            EncodedStreamInfo original = H264MetadataTestInput.Stream(H264MetadataTestInput.Configuration(
                [H264MetadataTestInput.Sequence()], [H264MetadataTestInput.Picture()]));
            EncodedStreamInfo stream = new(original.StreamIndex, original.Kind, original.CodecId, original.Format,
                original.TimeBase, original.Configuration.Span, colorDescription: color);
            using MpegTsTestOutput output = new();
            MediaBufferAllocator allocator = new(8192);
            Assert.Throws<NotSupportedException>(() => MpegTsWriter.Create(output, [stream], allocator));
            Assert.Equal(0, output.Writes);
            Assert.Equal(0, allocator.AllocatedBytes);
        }

    }
}
