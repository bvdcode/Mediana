// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Formats;
using Mediana.Tests;

namespace Mediana.Codecs.Av1.Tests
{
    public class Av1IntraBlockCopyPictureTests
    {
        [Theory]
        [InlineData("intrabcColor", 0, false, true)]
        [InlineData("intrabcOdd", 0, false, true)]
        [InlineData("intrabcMono", 0, true, true)]
        [InlineData("intrabcSb128", 4, false, true)]
        [InlineData("intrabcTiles", 4, false, true)]
        [InlineData("intrabcMixed", 0, false, false)]
        [InlineData("intrabcLossless", 0, false, true)]
        public void ReconstructsIndependentPicturesAndResets(string fixture, byte level, bool monochrome, bool allKeyFrames)
        {
            ReadOnlySpan<int> randomAccess = allKeyFrames ? [0, 1, 2] : default;
            Av1PictureSequenceTestInput.Verify(Data, fixture, [0x81, level, monochrome ? (byte)28 : (byte)12, 0], randomAccess);
        }

        [Theory]
        [InlineData("intrabcColor", false, 1, 1)]
        [InlineData("intrabcSb128", true, 1, 1)]
        [InlineData("intrabcTiles", false, 2, 2)]
        public void IndependentHeadersActivateIntraBlockCopyAndTheActualTileGrid(string fixture, bool superblock128, int columns, int rows)
        {
            byte[] encoded = File.ReadAllBytes(Path.Combine(Data, fixture + ".obu"));
            Av1FrameUnitReader units = new(encoded);
            Assert.True(units.TryRead(out ReadOnlySpan<byte> unit, CancellationToken.None));
            MediaBufferAllocator allocator = new(16 * 1024 * 1024);
            using (Av1PictureDecoder decoder = new(allocator))
            using (Av1Picture picture = decoder.Decode(unit))
            {
                Assert.True(picture.Header.Intrabc);
                Assert.Equal(superblock128, picture.Sequence.Superblock128);
                Assert.Equal(columns, picture.Header.Tiles.Columns);
                Assert.Equal(rows, picture.Header.Tiles.Rows);
                Assert.False(picture.Header.LoopFilter.Enabled);
                Assert.False(picture.Header.Cdef.Enabled);
                Assert.False(picture.Header.Restoration.Enabled);
                Assert.False(picture.Header.Deltas.FilterPresent);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void RetainedOutputIsIndependentFromLaterIntraCopiesAndReset() =>
            Av1PictureSequenceTestInput.VerifyOwnership(Data, "intrabcMixed", [0x81, 0, 12, 0]);

        [Fact]
        public void TheMotionGridUsesTheCallerBudgetAndReleasesPartialFrameAllocations()
        {
            byte[] encoded = File.ReadAllBytes(Path.Combine(Data, "intrabcColor.obu"));
            ReadOnlyMemory<byte> first = Av1PictureSequenceTestInput.Packets(encoded)[0];
            MediaBufferAllocator storage = new(1024 * 1024);
            using WritableMediaBuffer write = storage.Allocate(first.Length);
            first.Span.CopyTo(write.Span);
            using MediaBuffer buffer = write.Publish();
            using MediaPacket packet = new(0, buffer);
            EncodedStreamInfo stream = new(0, MediaKind.Video, "av1", StreamFormats.Av1, new(1, 6), [0x81, 0, 12, 0]);
            MediaBufferAllocator allocator = new(512 * 192 * 3 / 2 + 1);
            using IMediaCodec<MediaPacket, VideoFrame> decoder = new Av1DecoderFactory().Create(stream, allocator);
            Assert.Throws<MediaMemoryLimitException>(() => decoder.TrySend(packet));
            Assert.Equal(0, allocator.AllocatedBytes);
            decoder.Reset();
            Assert.Throws<OperationCanceledException>(() => decoder.TrySend(packet, new(true)));
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        private static string Data => Path.Combine(AppContext.BaseDirectory, "Data");
    }
}
