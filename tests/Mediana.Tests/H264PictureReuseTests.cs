// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    public class H264PictureReuseTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void AmortizedAllocationAndRetainedPacketsRemainIndependentOfReconstruction(bool intraOnly) => H264PictureReuseTestInput.Verify(intraOnly);

        [Fact]
        public void ResetClearsCodingContextsWithoutReallocatingOrChangingPixels()
        {
            MediaBufferAllocator allocator = new(65536);
            using H264Encoder encoder = H264Encoder.Create(H264EncoderTests.Format(32, 32), new(1, 1000), allocator);
            H264SequenceParameterSet sequence = H264Configuration.Parse(encoder.Stream.Configuration.Span, new()).Sequences[0];
            using H264Picture picture = new(sequence, allocator, H264SliceKind.P);
            picture.FirstMacroblock = 3;
            picture.Modes.Fill(42);
            picture.Quantizers.Fill(51);
            picture.Transforms8x8.Fill(1);
            picture.ReferenceIndices(H264ReferenceList.L0).Fill(7);
            picture.MotionX(H264ReferenceList.L0).Fill(-37);
            picture.MotionY(H264ReferenceList.L0).Fill(25);
            for (int plane = 0; plane < 3; plane++)
            {
                picture.Counts(plane).Fill(16);
                picture.Plane(plane).Fill((byte)(100 + plane));
            }
            long held = allocator.AllocatedBytes;
            picture.ResetForEncoding();
            Assert.Equal(held, allocator.AllocatedBytes);
            Assert.Equal(0, picture.FirstMacroblock);
            Assert.False(picture.Modes.ContainsAnyExcept(byte.MaxValue));
            Assert.False(picture.ReferenceIndices(H264ReferenceList.L0).ContainsAnyExcept(byte.MaxValue));
            Assert.False(picture.Quantizers.ContainsAnyExcept((byte)0));
            Assert.False(picture.Transforms8x8.ContainsAnyExcept((byte)0));
            Assert.False(picture.MotionX(H264ReferenceList.L0).ContainsAnyExcept(0));
            Assert.False(picture.MotionY(H264ReferenceList.L0).ContainsAnyExcept(0));
            Assert.False(picture.MotionAt(H264ReferenceList.L0, 0, 0).Available);
            for (int plane = 0; plane < 3; plane++)
            {
                Assert.False(picture.Counts(plane).ContainsAnyExcept((byte)0));
                Assert.False(picture.Plane(plane).ContainsAnyExcept((byte)(100 + plane)));
            }
        }

        [Fact]
        public void PublishedAndBidirectionalPicturesCannotEnterTheEncoderWorkspace()
        {
            MediaBufferAllocator allocator = new(65536);
            using H264Encoder encoder = H264Encoder.Create(H264EncoderTests.Format(32, 32), new(1, 1000), allocator);
            H264SequenceParameterSet sequence = H264Configuration.Parse(encoder.Stream.Configuration.Span, new()).Sequences[0];
            using H264Picture picture = new(sequence, allocator, H264SliceKind.P);
            picture.Plane(0).Fill(73);
            using MediaBuffer published = picture.PublishPixels();
            Assert.Throws<InvalidOperationException>(picture.ResetForEncoding);
            Assert.False(published.Memory.Span[..1024].ContainsAnyExcept((byte)73));
            using H264Picture bidirectional = new(sequence, allocator, H264SliceKind.B);
            Assert.Throws<InvalidOperationException>(bidirectional.ResetForEncoding);
            picture.Dispose();
            Assert.Throws<ObjectDisposedException>(picture.ResetForEncoding);
            Assert.False(published.Memory.Span[..1024].ContainsAnyExcept((byte)73));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void FailureAfterSeveralPicturesReleasesBothWorkspacesAndPreservesCallerStorage(bool cancel)
        {
            MediaBufferAllocator allocator = new(65536);
            VideoFormat format = H264EncoderTests.Format(32, 32);
            using H264Encoder encoder = H264Encoder.Create(format, new(1, 1000), allocator);
            for (int index = 0; index < 4; index++)
            {
                using VideoFrame frame = H264EncoderTests.Frame(format, allocator, index);
                encoder.TrySend(frame);
                using MediaPacket packet = encoder.Receive().Value!;
            }
            using VideoFrame input = H264EncoderTests.Frame(format, allocator, 4);
            using CancellationTokenSource cancellation = new();
            WritableMediaBuffer? occupied = null;
            if (cancel)
            {
                cancellation.Cancel();
                Assert.ThrowsAny<OperationCanceledException>(() => encoder.TrySend(input, cancellation.Token));
            }
            else
            {
                occupied = allocator.Allocate(checked((int)(allocator.LimitBytes - allocator.AllocatedBytes)));
                Assert.Throws<MediaMemoryLimitException>(() => encoder.TrySend(input));
            }
            occupied?.Dispose();
            Assert.Equal(input.Data.Length, allocator.AllocatedBytes);
            encoder.Reset();
            encoder.TrySend(input);
            using MediaPacket replay = encoder.Receive().Value!;
            Assert.True(replay.IsKeyFrame);
            encoder.CompleteInput();
            encoder.Dispose();
            replay.Dispose();
            input.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
