// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    public class H264StationaryHistoryTests
    {
        [Theory]
        [InlineData(0, 16, 256)]
        [InlineData(1, 8, 64)]
        [InlineData(2, 8, 64)]
        public void RunsEndAtTheFirstChangedVisibleSampleOrIneligibleBlock(int plane, int size, int length)
        {
            MediaBufferAllocator allocator = new(65536);
            VideoFormat format = H264EncoderTests.Format(46, 34);
            using VideoFrame frame = H264TemporalEncodingTestInput.CreateFrame(format, allocator, 3, 2,
                new(0, new(1, 25)), new(1, new(1, 25)));
            H264SourcePicture source = new(frame);
            using H264StationaryHistory history = new(3, 3, allocator);
            Span<byte> samples = stackalloc byte[H264SourceMacroblock.ByteCount];
            const int y = 32;
            for (int column = 0; column < 3; column++)
            {
                source.Read(column * 16, y, samples);
                history.Record(6 + column, samples, true);
            }
            Assert.Equal(3, history.MatchingRun(source, 6, 0, y));
            Assert.Equal(2, history.MatchingRun(source, 7, 16, y));

            int offset = plane == 0 ? 0 : 256 + (plane - 1) * length;
            int scale = 16 / size;
            PlaneLayout layout = frame.Planes[plane];
            int lastRow = layout.Height - 1 - y / scale;
            int lastColumn = layout.RowBytes - 1 - 32 / scale;
            samples[offset + lastRow * size + lastColumn] ^= 1;
            history.Record(8, samples, true);
            Assert.Equal(2, history.MatchingRun(source, 6, 0, y));

            history.Record(7, samples, false);
            Assert.Equal(1, history.MatchingRun(source, 6, 0, y));
            history.Reset();
            Assert.Equal(0, history.MatchingRun(source, 6, 0, y));
        }
    }
}
