// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Formats.Video;
using Mediana.Tests;

namespace Mediana.Codecs.Mpeg4Visual.Tests
{
    public class IndependentReconstructionTests
    {
        [Fact]
        public void PartialMacroblockReferenceEdgesMatchIndependentReconstruction()
        {
            const int frameCount = 37;
            const int frameBytes = 96 * 72 * 3 / 2;
            string root = Path.Combine(AppContext.BaseDirectory, "Data");
            byte[] encoded = File.ReadAllBytes(Path.Combine(root, "partial-macroblock.m4v"));
            byte[] expected = File.ReadAllBytes(Path.Combine(root, "partial-macroblock.yuv"));
            Mpeg4VisualConfiguration configuration = Mpeg4VisualConfiguration.Read(encoded);
            Assert.Equal(96, configuration.Width);
            Assert.Equal(72, configuration.Height);
            Assert.False(configuration.QuarterPel);
            Assert.Equal(frameCount * frameBytes, expected.Length);

            MediaBufferAllocator allocator = new(4 * 1024 * 1024, MemoryPool<byte>.Shared);
            using IMediaCodec<MediaPacket, VideoFrame> decoder = new Mpeg4DecoderFactory().Create(Mpeg4TestInput.Stream(encoded, true), allocator);
            int count = 0;
            foreach (byte[] bytes in Mpeg4TestInput.Packets(encoded))
            {
                using MediaPacket packet = Mpeg4TestInput.Packet(allocator, bytes);
                Assert.Equal(CodecInputStatus.Accepted, decoder.TrySend(packet));
                Receive(CodecOutputStatus.InputRequired);
            }
            decoder.CompleteInput();
            Receive(CodecOutputStatus.EndOfStream);
            Assert.Equal(frameCount, count);
            decoder.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);

            void Receive(CodecOutputStatus required)
            {
                CodecOutput<VideoFrame> output = decoder.Receive();
                while (output.Status == CodecOutputStatus.Available)
                {
                    using VideoFrame frame = output.Value;
                    Assert.InRange(count, 0, frameCount - 1);
                    Assert.Equal(VideoPixelFormat.Yuv420P8, frame.Format.PixelFormat);
                    Assert.Equal(96, frame.Format.Width);
                    Assert.Equal(72, frame.Format.Height);
                    Mpeg4TestInput.VerifyPixels(frame, expected.AsSpan(count++ * frameBytes, frameBytes));
                    output = decoder.Receive();
                }
                Assert.Equal(required, output.Status);
            }
        }
    }
}
