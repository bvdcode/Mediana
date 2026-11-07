// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Jpeg;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    public class JpegIccTests
    {
        [Fact]
        public void OutOfOrderProfileFragmentsAreOwnedByTheReturnedFrame()
        {
            byte[] profile = Enumerable.Range(0, 140).Select(value => (byte)value).ToArray();
            byte[] image = JpegTestData.Insert(JpegTestData.Image(), 226, [.. "ICC_PROFILE\0"u8, 1, 2, .. profile.AsSpan(0, 73)]);
            image = JpegTestData.Insert(image, 226, [.. "ICC_PROFILE\0"u8, 2, 2, .. profile.AsSpan(73)]);
            MediaBufferAllocator allocator = new(4096);
            using (VideoFrame frame = new JpegImageDecoder().Decode(image, new(), allocator))
            {
                IccColorDescription color = Assert.IsType<IccColorDescription>(frame.Format.ColorDescription);
                image.AsSpan().Clear();
                Assert.Equal(profile, color.Profile.ToArray());
                Assert.False(color.Restricted);
                Assert.Throws<NotSupportedException>(() => frame.Format.GetColorInterpretation());
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
