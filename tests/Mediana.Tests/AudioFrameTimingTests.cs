// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;

namespace Mediana.Tests
{
    public class AudioFrameTimingTests
    {
        [Theory]
        [InlineData(AudioSampleFormat.Unsigned8)]
        [InlineData(AudioSampleFormat.Signed16)]
        [InlineData(AudioSampleFormat.Signed32)]
        [InlineData(AudioSampleFormat.Float32)]
        [InlineData(AudioSampleFormat.Float64)]
        public void RetimingPreservesBothLayoutsWithoutCopyingPcm(AudioSampleFormat format)
        {
            AudioFrameTimingTestInput.Verify(format, AudioStorageLayout.Interleaved);
            AudioFrameTimingTestInput.Verify(format, AudioStorageLayout.Planar);
        }

        [Fact]
        public void InvalidTimestampAndDisposedInputDoNotCreateAnAdditionalLease()
        {
            MediaBufferAllocator allocator = new(64);
            AudioFormat format = new(8000, AudioSampleFormat.Signed16, AudioStorageLayout.Interleaved, "mono");
            using AudioFrame input = AudioSamplePreparationTestInput.CreateFrame(format, 8, allocator);
            Assert.Throws<ArgumentException>(() => input.RetainWithTimestamp(default(MediaTimestamp)));
            Assert.Equal(16, allocator.AllocatedBytes);
            Assert.NotNull(input.Timestamp);
            input.Dispose();
            Assert.Throws<ObjectDisposedException>(() => input.RetainWithTimestamp(null));
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
