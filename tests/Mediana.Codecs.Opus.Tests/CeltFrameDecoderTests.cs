// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Formats.Audio;
using Mediana.Tests;

namespace Mediana.Codecs.Opus.Tests
{
    public class CeltFrameDecoderTests
    {
        [Fact]
        public void SequentialPcmMatchesIndependentDecodingAcrossChannelBandwidthAndFrameSizeChanges() =>
            Assert.Equal(2448, OpusCeltPcmTestInput.Verify(Path.Combine(AppContext.BaseDirectory, "Data")));

        [Fact]
        public void RepeatedSilentFramesAllocateNoManagedStorageAndResetIsDeterministic()
        {
            MediaBufferAllocator allocator = new(256 * 1024);
            using CeltTransformPlan plan = CeltTransformPlan.Create(allocator);
            using CeltPulseCodebook codebook = CeltPulseCodebook.Create(176, 128, allocator);
            using CeltFrameDecoder decoder = new(plan, codebook, allocator, 2, false);
            CeltBandMode mode = new(1, 3, 0, 21);
            Span<float> output = stackalloc float[1920], initial = stackalloc float[1920];
            ReadOnlySpan<byte> silence = [255, 254];
            decoder.Decode(silence, mode, initial);
            uint initialRange = decoder.FinalRange;
            long before = GC.GetAllocatedBytesForCurrentThread(), allocated = allocator.AllocatedBytes;
            for (int at = 0; at < 100; at++) { decoder.Decode(silence, mode, output); }
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
            Assert.Equal(allocated, allocator.AllocatedBytes);
            decoder.Reset(); decoder.Decode(silence, mode, output);
            Assert.True(output.SequenceEqual(initial));
            Assert.Equal(initialRange, decoder.FinalRange);
            decoder.Dispose(); codebook.Dispose(); plan.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void CancellationBeforeDecodePreservesOutputAndDecoderState()
        {
            MediaBufferAllocator allocator = new(256 * 1024);
            using CeltTransformPlan plan = CeltTransformPlan.Create(allocator);
            using CeltPulseCodebook codebook = CeltPulseCodebook.Create(176, 128, allocator);
            using CeltFrameDecoder decoder = new(plan, codebook, allocator, 1, true);
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();
            float[] output = Enumerable.Repeat(17f, 960).ToArray();
            Assert.Throws<OperationCanceledException>(() => decoder.Decode(new byte[] { 255, 254 }, new(1, 3, 0, 21), output, cancellation.Token));
            Assert.All(output, value => Assert.Equal(17, value));
            Assert.Equal(0U, decoder.FinalRange);
            Assert.Equal(960, decoder.Decode(new byte[] { 255, 254 }, new(1, 3, 0, 21), output));
        }

        [Fact]
        public void UnsupportedModeDoesNotConsumeDecoderState()
        {
            MediaBufferAllocator allocator = new(256 * 1024);
            using CeltTransformPlan plan = CeltTransformPlan.Create(allocator);
            using CeltPulseCodebook codebook = CeltPulseCodebook.Create(176, 128, allocator);
            using CeltFrameDecoder decoder = new(plan, codebook, allocator, 1, true);
            float[] output = new float[960];
            Assert.Throws<NotSupportedException>(() => decoder.Decode(new byte[] { 255, 254 }, new(1, 3, 17, 21), output));
            Assert.Throws<ArgumentException>(() => CeltBandMode.FromConfiguration(OpusPacketConfiguration.FromToc(0)));
            Assert.Equal(0U, decoder.FinalRange);
            Assert.Equal(960, decoder.Decode(new byte[] { 255, 254 }, new(1, 3, 0, 21), output));
        }
    }
}
