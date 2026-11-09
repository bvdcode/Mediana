// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public class H264CabacPictureEncoderTests
    {
        [Theory]
        [InlineData(0, 0)]
        [InlineData(0, 26)]
        [InlineData(1, 0)]
        [InlineData(1, 12)]
        [InlineData(1, 26)]
        [InlineData(1, 38)]
        [InlineData(1, 51)]
        [InlineData(2, 0)]
        [InlineData(2, 26)]
        [InlineData(2, 51)]
        [InlineData(3, 26)]
        public async Task CompleteCabacSequencePreservesPicturesTimingAndRestartBoundaries(int mode, int qp)
        {
            await H264CabacPictureTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "H264CabacEncoding"), mode, qp);
        }

        [Theory]
        [InlineData(1, 0, 32000, 200)]
        [InlineData(1, 26, 32000, 200)]
        [InlineData(3, 51, 40000, 300)]
        public async Task RateControlledCabacPreservesTheCompleteIndependentSequence(int mode, int qp, int bitRate, int bufferMilliseconds)
        {
            await H264CabacPictureTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "H264CabacEncoding"), mode, qp,
                new() { BitRate = bitRate, BufferDuration = TimeSpan.FromMilliseconds(bufferMilliseconds) });
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(2)]
        public void InvalidEntropyCodingFailsBeforeAllocating(int entropy)
        {
            MediaBufferAllocator allocator = new(4096);
            Assert.Throws<ArgumentOutOfRangeException>(() => H264Encoder.Create(H264EncoderTests.Format(16, 16),
                new(1, 25), allocator, new() { EntropyCoding = (H264EntropyCoding)entropy }));
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
