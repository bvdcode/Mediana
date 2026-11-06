// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Tests;

namespace Mediana.Codecs.Opus.Tests
{
    public class CeltStereoBandTests
    {
        [Fact]
        public void JointStereoBandsMatchTheIndependentDecoder() =>
            Assert.Equal(7708, OpusSpectralTestInput.VerifyStereoBands(Path.Combine(AppContext.BaseDirectory, "Data")));

        [Fact]
        public void JointStereoReconstructionDoesNotAllocatePerBand()
        {
            MediaBufferAllocator allocator = new(128 * 1024);
            using CeltPulseCodebook codebook = CeltPulseCodebook.Create(176, 128, allocator);
            byte[] bytes = new byte[256];
            Span<float> left = stackalloc float[176], right = stackalloc float[176], low = stackalloc float[176], output = stackalloc float[176];
            low.Fill(.125f);
            OpusRangeDecoder warm = new(bytes);
            CeltBandDecoderState state = new(codebook, 20, CeltSpreading.Normal, 4096, 17);
            CeltStereoBandReader.Read(ref warm, ref state, left, right, 1200, new(3, 8, -1), low, output, 255);
            long before = GC.GetAllocatedBytesForCurrentThread();
            uint masks = 0;
            for (int at = 0; at < 1000; at++)
            {
                OpusRangeDecoder decoder = new(bytes);
                state = new(codebook, 20, CeltSpreading.Normal, 4096, 17);
                masks |= CeltStereoBandReader.Read(ref decoder, ref state, left, right, 1200, new(3, 8, -1), low, output, 255);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(0, allocated);
            Assert.NotEqual(0u, masks);
            foreach (float coefficient in low) { Assert.Equal(.125f, coefficient); }
        }
    }
}
