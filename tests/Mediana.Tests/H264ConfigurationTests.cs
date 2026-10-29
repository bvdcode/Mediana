// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Codecs.H264;
using Mediana.Containers.Mp4;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public class H264ConfigurationTests
    {
        [Theory]
        [InlineData("avc-tail.mov", 66, 80, 48, false)]
        [InlineData("avc-aac.mp4", 100, 64, 48, true)]
        public async Task ParsesRealAvcConfiguration(string file, int profile, int width, int height, bool cabac)
        {
            byte[] configuration = await ReadConfigurationAsync(file);
            H264Configuration parsed = H264Configuration.Parse(configuration, new H264DecoderOptions());
            H264SequenceParameterSet sequence = Assert.Single(parsed.Sequences.Values);
            H264PictureParameterSet picture = Assert.Single(parsed.Pictures.Values);
            Assert.Equal(4, parsed.LengthSize);
            Assert.Equal(profile, sequence.Profile);
            Assert.Equal(width, sequence.Width);
            Assert.Equal(height, sequence.Height);
            Assert.Equal(8, sequence.LumaBitDepth);
            Assert.Equal(8, sequence.ChromaBitDepth);
            Assert.Equal(1, sequence.ChromaFormat);
            Assert.True(sequence.FrameMbsOnly);
            Assert.Equal(sequence.Id, picture.SequenceId);
            Assert.Equal(cabac, picture.Cabac);
        }

        [Fact]
        public async Task PreservesRealBaselineVuiMetadata()
        {
            H264Configuration configuration = H264Configuration.Parse(await ReadConfigurationAsync("avc-tail.mov"), new H264DecoderOptions());
            H264SequenceParameterSet sequence = Assert.Single(configuration.Sequences.Values);
            H264VuiParameters vui = Assert.IsType<H264VuiParameters>(sequence.Vui);
            Assert.Equal(1, vui.AspectRatioWidth);
            Assert.Equal(1, vui.AspectRatioHeight);
            Assert.Equal(1u, vui.UnitsInTick);
            Assert.Equal(10u, vui.TimeScale);
            Assert.Equal(0, vui.MaxReorderFrames);
            Assert.Equal(3, vui.MaxDecodedFrameBuffering);
            Assert.False(vui.FullRange);
            Assert.Equal(2, vui.ColorPrimaries);
            Assert.Equal(2, vui.TransferCharacteristics);
            Assert.Equal(2, vui.MatrixCoefficients);
        }

        [Fact]
        public async Task RejectsTruncatedConfigurationsAndReservedLengthSize()
        {
            byte[] configuration = await ReadConfigurationAsync("avc-tail.mov");
            for (int length = 0; length < configuration.Length; length++)
            {
                byte[] truncated = configuration[..length];
                Assert.Throws<InvalidDataException>(() => H264Configuration.Parse(truncated, new H264DecoderOptions()));
            }
            configuration[4] = 0xFE;
            Assert.Throws<InvalidDataException>(() => H264Configuration.Parse(configuration, new H264DecoderOptions()));
        }

        [Fact]
        public async Task ReplacesInBandParameterSetsWithoutGrowingRepeatedConfigurationOrMutatingPriorState()
        {
            byte[] data = await ReadConfigurationAsync("avc-tail.mov");
            H264DecoderOptions options = new() { MaxConfigurationBytes = data.Length };
            H264Configuration original = H264Configuration.Parse(data, options);
            int sequenceLength = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(6));
            byte[] sequence = data.AsSpan(8, sequenceLength).ToArray();
            int pictureStart = 8 + sequenceLength + 1;
            int pictureLength = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(pictureStart));
            byte[] picture = data.AsSpan(pictureStart + 2, pictureLength).ToArray();
            H264Configuration updated = original;
            for (int index = 0; index < 100; index++)
            {
                updated = updated.WithParameterSet(sequence, options).WithParameterSet(picture, options);
            }
            Assert.Equal(original.Sequences[0], updated.Sequences[0]);
            Assert.Equal(original.Pictures[0], updated.Pictures[0]);
            Assert.Throws<NotSupportedException>(() => updated.WithParameterSet(sequence,
                options with { MaxConfigurationBytes = data.Length - 1 }));
            Assert.Throws<NotSupportedException>(() => updated.WithParameterSet(picture,
                options with { MaxParameterSetBytes = 2 }));
            Assert.Throws<InvalidDataException>(() => updated.WithParameterSet(sequence.AsSpan(0, sequence.Length - 1), options));
            byte[] replacement = await ReadConfigurationAsync("avc-weighted.mp4");
            int replacementLength = BinaryPrimitives.ReadUInt16BigEndian(replacement.AsSpan(6));
            H264Configuration changed = updated.WithParameterSet(replacement.AsSpan(8, replacementLength), new H264DecoderOptions());
            Assert.Equal(80, original.Sequences[0].Width);
            Assert.Equal(94, changed.Sequences[0].Width);
            Assert.Single(changed.Sequences);
            Assert.Single(changed.Pictures);
        }

        [Fact]
        public async Task LimitsConfigurationParameterSetAndPictureSizes()
        {
            byte[] configuration = await ReadConfigurationAsync("avc-tail.mov");
            Assert.Throws<NotSupportedException>(() => H264Configuration.Parse(configuration,
                new H264DecoderOptions { MaxConfigurationBytes = 8 }));
            Assert.Throws<NotSupportedException>(() => H264Configuration.Parse(configuration,
                new H264DecoderOptions { MaxParameterSetBytes = 2 }));
            Assert.Throws<NotSupportedException>(() => H264Configuration.Parse(configuration,
                new H264DecoderOptions { MaxPicturePixels = 16 }));
        }

        private static async Task<byte[]> ReadConfigurationAsync(string name)
        {
            await using TrackingRandomAccessSource source = new(await Mp4TestData.ReadAsync(name));
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator);
            return reader.Streams[0].Configuration.ToArray();
        }
    }
}
