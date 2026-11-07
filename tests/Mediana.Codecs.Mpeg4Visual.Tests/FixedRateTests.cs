// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.Formats.Video;
using Mediana.Tests;

namespace Mediana.Codecs.Mpeg4Visual.Tests
{
    public class FixedRateTests
    {
        [Fact]
        public void FixedPicturePeriodMayEqualTheClockResolution()
        {
            string root = Path.Combine(AppContext.BaseDirectory, "Data");
            byte[] encoded = File.ReadAllBytes(Path.Combine(root, "fixed-one-fps.m4v"));
            byte[] expected = File.ReadAllBytes(Path.Combine(root, "fixed-one-fps.yuv"));
            Mpeg4VisualConfiguration configuration = Mpeg4VisualConfiguration.Read(encoded);
            Assert.Equal(1, configuration.TimeIncrementResolution);
            Assert.Equal(1, configuration.FixedTimeIncrement);
            Assert.Equal(16, configuration.Width);
            Assert.Equal(16, configuration.Height);
            MediaBufferAllocator allocator = new(1024 * 1024);
            using IMediaCodec<MediaPacket, VideoFrame> decoder = new Mpeg4DecoderFactory().Create(Mpeg4TestInput.Stream(encoded, true), allocator);
            int count = 0;
            foreach (byte[] bytes in Mpeg4TestInput.Packets(encoded))
            {
                using MediaPacket packet = Mpeg4TestInput.Packet(allocator, bytes, duration: new(1, new(1, 1)));
                Assert.Equal(CodecInputStatus.Accepted, decoder.TrySend(packet));
                CodecOutput<VideoFrame> output = decoder.Receive();
                while (output.Status == CodecOutputStatus.Available)
                {
                    using VideoFrame frame = output.Value;
                    Assert.Equal(new MediaTimestamp(count, new(1, 1)), frame.Timestamp);
                    Assert.Equal(new MediaDuration(1, new(1, 1)), frame.Duration);
                    Mpeg4TestInput.VerifyPixels(frame, expected.AsSpan(count++ * 384, 384));
                    output = decoder.Receive();
                }
                Assert.Equal(CodecOutputStatus.InputRequired, output.Status);
            }
            decoder.CompleteInput();
            Assert.Equal(CodecOutputStatus.EndOfStream, decoder.Receive().Status);
            Assert.Equal(3, count);
            decoder.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
