// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.MpegTs;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using static Mediana.Tests.AvcFramingTestData;

namespace Mediana.Tests
{
    public class AvcAspectRatioFramingTests
    {
        [Theory]
        [InlineData(false, false, 4, 3)]
        [InlineData(true, false, 4, 3)]
        [InlineData(false, true, 1, 1)]
        [InlineData(true, true, 1, 1)]
        public void WritesSourceGeometryWithAndWithoutExistingVui(bool hasVui, bool square, int width, int height)
        {
            const string signal = "1 011 1 1 00000110 00001110 00000101";
            const string afterSignal = "1 011 00100 1 00000000000000000000000000000001 00000000000000000000000000110010 1 0000";
            string? vui = hasVui ? "1 00000010 1 0 " + signal + " " + afterSignal : null;
            byte[] original = H264MetadataTestInput.Sequence(vui: vui);
            SampleAspectRatio ratio = new(width, height);
            MediaBufferAllocator allocator = new(8192);
            AvcAnnexBConverter converter = new(H264MetadataTestInput.Configuration([original], [H264MetadataTestInput.Picture()]),
                allocator: allocator, aspectRatioDescription: new(ratio));
            byte[] slice = [0x65, 0x9a];
            using (MediaBuffer result = converter.Convert(Packet(4, original, slice), allocator, 4096, out bool idr))
            {
                Assert.True(idr);
                byte[][] sequences = Nals(result.Memory.Span).Where(nal => (nal[0] & 31) == 7).ToArray();
                Assert.Equal(2, sequences.Length);
                string spacing = square ? "1 00000001" : "1 11111111 0000000000000100 0000000000000011";
                string expectedVui = hasVui ? spacing + " 1 0 " + signal + " " + afterSignal : spacing + " 00 000000";
                byte[] expected = H264MetadataTestInput.Sequence(vui: expectedVui);
                Assert.All(sequences, nal => Assert.Equal(expected, nal));
                Assert.Equal(slice, Nals(result.Memory.Span)[^1]);
                H264SequenceParameterSet before = H264SequenceParser.Parse(original);
                H264SequenceParameterSet after = H264SequenceParser.Parse(sequences[0]);
                Assert.Equal(before with { Vui = null }, after with { Vui = null });
                if (hasVui)
                {
                    Assert.Equal(before.Vui! with { AspectRatioWidth = width, AspectRatioHeight = height }, after.Vui);
                }
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void FormatDefaultsApplyToEachSpsWithoutOverridingExplicitGeometry()
        {
            byte[] implicitGeometry = H264MetadataTestInput.Sequence();
            byte[] explicitGeometry = H264MetadataTestInput.Sequence(1, vui: "1 00001110 00000000");
            byte[] configuration = H264MetadataTestInput.Configuration([implicitGeometry, explicitGeometry], [H264MetadataTestInput.Picture()]);
            MediaBufferAllocator allocator = new(8192);
            AvcAnnexBConverter converter = new(configuration, allocator: allocator,
                aspectRatioDescription: new(SampleAspectRatio.Square, isDefault: true));
            using (MediaBuffer result = converter.Convert(Packet(4, [0x65, 0x9a]), allocator, 4096, out _))
            {
                byte[][] sequences = Nals(result.Memory.Span).Where(nal => (nal[0] & 31) == 7).ToArray();
                Assert.Equal(H264MetadataTestInput.Sequence(vui: "1 00000001 00000000"), sequences[0]);
                Assert.Equal(explicitGeometry, sequences[1]);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void ColorAndGeometryAreWrittenTogetherWithoutChangingOtherDeclarations()
        {
            byte[] sequence = H264MetadataTestInput.Sequence(vui: "1 00000010 1 0 1 011 1 0 000000");
            byte[] configuration = H264MetadataTestInput.Configuration([sequence], [H264MetadataTestInput.Picture()]);
            MediaBufferAllocator allocator = new(8192);
            AvcAnnexBConverter converter = new(configuration, new CicpColorDescription(9, 16, 9), allocator,
                aspectRatioDescription: new(SampleAspectRatio.Square));
            using (MediaBuffer result = converter.Convert(Packet(4, [0x65, 0x9a]), allocator, 4096, out _))
            {
                byte[] actual = Assert.Single(Nals(result.Memory.Span), nal => (nal[0] & 31) == 7);
                Assert.Equal(H264MetadataTestInput.Sequence(vui: "1 00000001 1 0 1 011 1 1 00001001 00010000 00001001 000000"), actual);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void UnrepresentableGeometryFailsBeforeDestinationIoAndReleasesStorage()
        {
            EncodedStreamInfo original = H264MetadataTestInput.Stream(H264MetadataTestInput.Configuration(
                [H264MetadataTestInput.Sequence()], [H264MetadataTestInput.Picture()]));
            EncodedStreamInfo stream = new(original.StreamIndex, original.Kind, original.CodecId, original.Format,
                original.TimeBase, original.Configuration.Span, aspectRatioDescription: new(new SampleAspectRatio(65536, 1)));
            using MpegTsTestOutput output = new();
            MediaBufferAllocator allocator = new(8192);
            Assert.Throws<NotSupportedException>(() => MpegTsWriter.Create(output, [stream], allocator));
            Assert.Equal(0, output.Writes);
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.Throws<ArgumentNullException>(() => new AvcAnnexBConverter(original.Configuration,
                aspectRatioDescription: new(SampleAspectRatio.Square)));
        }
    }
}
