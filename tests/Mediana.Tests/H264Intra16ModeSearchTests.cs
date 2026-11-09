// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    public class H264Intra16ModeSearchTests
    {
        [Theory]
        [InlineData(0, 0)]
        [InlineData(16, 0)]
        [InlineData(0, 16)]
        [InlineData(16, 16)]
        [InlineData(32, 48)]
        public void BoundedVectorScoringPreservesAvailableModesAndTieOrder(int x, int y)
        {
            MediaBufferAllocator allocator = new(65536);
            VideoFormat format = H264EncoderTests.Format(64, 64);
            using H264Encoder encoder = H264Encoder.Create(format, new(1, 1000), allocator);
            H264Configuration configuration = H264Configuration.Parse(encoder.Stream.Configuration.Span, new());
            using H264Picture picture = new(configuration.Sequences[0], allocator, H264SliceKind.I);
            picture.Modes.Fill(2);
            Span<byte> prediction = stackalloc byte[256];
            Span<byte> samples = stackalloc byte[384];
            Random random = new(41277);
            for (int iteration = 0; iteration < 80; iteration++)
            {
                if (iteration < 2) { picture.Plane(0).Fill((byte)(iteration * 128)); }
                else { random.NextBytes(picture.Plane(0)); }
                using VideoFrame frame = H264EncoderTests.Frame(format, allocator, iteration);
                H264SourceMacroblock source = new(frame, x, y, samples);
                if (iteration < 2) { samples.Fill((byte)(iteration * 128)); }
                int expected = 2;
                int minimum = int.MaxValue;
                for (int mode = 0; mode < 4; mode++)
                {
                    if ((mode is 0 or 3 && y == 0) || (mode is 1 or 3 && x == 0)) { continue; }
                    picture.PredictMacroblock(0, x, y, mode, prediction);
                    int difference = 0;
                    for (int index = 0; index < 256; index++) { difference += Math.Abs(source.Luma[index] - prediction[index]); }
                    if (difference < minimum) { minimum = difference; expected = mode; }
                }
                Assert.Equal(expected, H264Intra16ModeSearch.Select(source, picture, x, y, prediction));
            }
        }
    }
}
