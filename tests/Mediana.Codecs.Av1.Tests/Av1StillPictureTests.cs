// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;

namespace Mediana.Codecs.Av1.Tests
{
    public class Av1StillPictureTests
    {
        [Theory]
        [InlineData("flat")]
        [InlineData("palette")]
        public void ReconstructsIndependentLosslessPixels(string name)
        {
            byte[] encoded = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", name + ".obu"));
            byte[] expected = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", name + ".gray"));
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (Av1Picture picture = Av1StillPictureDecoder.Decode(encoded, allocator))
            {
                Assert.Equal(16, picture.Sequence.Width);
                Assert.Equal(16, picture.Sequence.Height);
                Assert.Equal(expected, picture.Pixels.Memory.ToArray());
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
