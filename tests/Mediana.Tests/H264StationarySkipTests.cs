// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    public class H264StationarySkipTests
    {
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void TexturedSkipChecksTheLastSampleOfEveryPlane(int changedPlane)
        {
            MediaBufferAllocator allocator = new(65536);
            VideoFormat format = H264EncoderTests.Format(32, 32);
            using H264Encoder encoder = H264Encoder.Create(format, new(1, 1000), allocator);
            H264Configuration configuration = H264Configuration.Parse(encoder.Stream.Configuration.Span, new());
            using H264Picture reference = new(configuration.Sequences[0], allocator, H264SliceKind.P);
            using VideoFrame frame = H264EncoderTests.Frame(format, allocator, 0);
            for (int plane = 0; plane < 3; plane++)
            {
                for (int y = 0; y < frame.Planes[plane].Height; y++)
                {
                    frame.GetRow(plane, y).CopyTo(reference.Plane(plane)[(y * reference.Stride(plane))..]);
                }
            }
            Span<byte> storage = stackalloc byte[H264SourceMacroblock.ByteCount];
            H264SourceMacroblock source = new(frame, 16, 16, storage);
            Assert.True(source.Luma.ContainsAnyExcept(source.Luma[0]));
            H264SkipResidualBounds bounds = H264ForwardTransform.GetSkipBounds(30);
            Assert.True(bounds.AllowsStationarySkip(source, reference, 16, 16));
            ref byte sample = ref reference.Plane(changedPlane)[^1];
            byte original = sample;
            sample = (byte)(original + 1);
            Assert.True(bounds.AllowsStationarySkip(source, reference, 16, 16));
            Assert.False(H264ForwardTransform.GetSkipBounds(0).AllowsStationarySkip(source, reference, 16, 16));
            sample = (byte)(original + 2);
            Assert.False(bounds.AllowsStationarySkip(source, reference, 16, 16));
        }
    }
}
