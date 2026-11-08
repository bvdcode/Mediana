// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Audio;
using Mediana.Core.Memory;

namespace Mediana.Codecs.Ac3.Tests
{
    public class Ac3TransientHistoryTests
    {
        [Fact]
        public void NormativeLocationAndLengthLimitsFitTheRetainedHistory()
        {
            for (int location = 0; location <= 4092; location += 4)
            {
                Ac3TransientEvent item = new(location, 255);
                Assert.InRange(item.PreNoiseLength, 256, 508);
                Assert.InRange(item.SynthesisLength, 768, 1020);
                Assert.InRange(item.CorrectionLength, 767, 1019);
                Assert.True(item.Transient - item.SourceStart <= 1528);
                Assert.True(item.TargetStart >= -767);
                Assert.True(4092 + item.CorrectionLength < Ac3TransientProcessing.HoldbackSamples);
                Assert.True(4092 + 1536 + 1276 < Ac3TransientProcessing.HistorySamples);
            }
        }

        [Fact]
        public void MaximumFutureBurstKeepsPublishedOutputImmutableAndBoundsStorage()
        {
            MediaBufferAllocator allocator = new(65536);
            AudioFormat format = new(48000, AudioSampleFormat.Float32, AudioStorageLayout.Interleaved, new[] { "front-center" });
            AudioFrame? retained = null;
            byte[]? original = null;
            int outputSamples = 0;
            long peak = 0;
            using (Ac3TransientProcessing processing = new(allocator, format, true))
            {
                for (int frame = 0; frame < 128; frame++)
                {
                    processing.BeginAccessUnit(256, 256, new(frame * 256, new(1, 48000)));
                    Span<float> pcm = processing.GetFrameStorage(256);
                    for (int sample = 0; sample < 256; sample++) { pcm[sample] = ((frame * 256 + sample) % 97) / 128f; }
                    processing.AppendFrame(256, new[] { frame < 32 ? 4092 : -1 }, new[] { 255 });
                    peak = Math.Max(peak, allocator.AllocatedBytes);
                    while (processing.HasOutput)
                    {
                        AudioFrame output = processing.Receive(); outputSamples += output.SamplesPerChannel;
                        if (retained is null) { retained = output; original = output.GetPlane(0).ToArray(); }
                        else { output.Dispose(); }
                    }
                }
                processing.CompleteInput();
                while (processing.HasOutput) { using AudioFrame output = processing.Receive(); outputSamples += output.SamplesPerChannel; }
                Assert.Equal(128 * 256, outputSamples); Assert.InRange(peak, 32768, 36 * 1024);
                Assert.Equal(original, retained!.GetPlane(0).ToArray());
            }
            Assert.Equal(1024, allocator.AllocatedBytes);
            retained!.Dispose(); Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(true, 1023)]
        [InlineData(false, 1024)]
        public void BufferAndRecoveryReservationFailureReleaseEveryOwnedBuffer(bool originalBeginning, int limit)
        {
            MediaBufferAllocator allocator = new(limit);
            AudioFormat format = new(48000, AudioSampleFormat.Float32, AudioStorageLayout.Interleaved, new[] { "front-center" });
            using (Ac3TransientProcessing processing = new(allocator, format, originalBeginning))
            { Assert.Throws<MediaMemoryLimitException>(() => processing.BeginAccessUnit(256, 256, null)); }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

    }
}
