// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Processing.Audio;
using Mediana.Core.Audio;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public class CliAudioPcmPreparationTests
    {
        [Fact]
        public void AacPreparationRetainsDecodedFloatStorage()
        {
            AudioFormat format = new(44100, AudioSampleFormat.Float32, AudioStorageLayout.Interleaved, "left", "right");
            MediaBufferAllocator source = new(8192);
            MediaBufferAllocator destination = new(1);
            using AudioFrame input = AudioSamplePreparationTestInput.CreateFrame(format, 1024, source);
            AudioEncodingPcm converter = new(format, null);
            using AudioFrame output = converter.Apply(input, destination, default);
            Assert.True(input.GetPlane(0).Equals(output.GetPlane(0)));
            input.Dispose();
            Assert.Equal(8192, source.AllocatedBytes);
            Assert.Equal(0, destination.AllocatedBytes);
            output.Dispose();
            Assert.Equal(0, source.AllocatedBytes);
        }
    }
}
