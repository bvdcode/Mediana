// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Codecs.H264;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    public class H264EncoderTests
    {
        [Theory]
        [InlineData("avc-multislice-cabac", 26, 18)]
        [InlineData("avc-multislice-cavlc", 38, 18)]
        [InlineData("avc-weighted", 18, 12)]
        public async Task EveryEncodedPictureMatchesTheIndependentDecoder(string name, int qp, int count) =>
            await H264EncodingTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Mp4"), name, qp, count);

        [Theory]
        [InlineData(32, 32, 0)]
        [InlineData(32, 32, 26)]
        [InlineData(30, 22, 38)]
        [InlineData(2, 2, 51)]
        public void EncodedPicturesAreIndependentlyRestartableAndPreserveFormat(int width, int height, int qp)
        {
            MediaBufferAllocator allocator = new(4 * 1024 * 1024);
            VideoFormat format = Format(width, height);
            using H264Encoder encoder = H264Encoder.Create(format, new(1, 1000), allocator, new() { Quantizer = qp }, streamIndex: 3);
            using VideoFrame input = Frame(format, allocator, 0);
            Assert.Equal(CodecOutputStatus.InputRequired, encoder.Receive().Status);
            Assert.Equal(CodecInputStatus.Accepted, encoder.TrySend(input));
            Assert.Equal(CodecInputStatus.OutputRequired, encoder.TrySend(input));
            using MediaPacket packet = encoder.Receive().Value!;
            Assert.True(new H264RandomAccessInspector(encoder.Stream).IsIdr(packet));
            Assert.Equal(3, packet.StreamIndex);
            Assert.Equal(input.Timestamp, packet.PresentationTime);
            Assert.Equal(packet.PresentationTime, packet.DecodeTime);
            Assert.Equal(input.Duration, packet.Duration);
            using IMediaCodec<MediaPacket, VideoFrame> decoder = new H264DecoderFactory().Create(encoder.Stream, allocator);
            decoder.TrySend(packet);
            decoder.CompleteInput();
            using VideoFrame decoded = decoder.Receive().Value!;
            Assert.Equal(format.Width, decoded.Format.Width);
            Assert.Equal(format.Height, decoded.Format.Height);
            Assert.Equal(format.Color, decoded.Format.Color);
            Assert.Equal(format.SampleAspectRatio, decoded.Format.SampleAspectRatio);
            if (qp <= 26)
            {
                double squared = 0;
                for (int index = 0; index < decoded.Data.Length; index++)
                {
                    double error = input.Data.Span[index] - decoded.Data.Span[index];
                    squared += error * error;
                }
                Assert.True(squared / decoded.Data.Length < 35);
            }
            encoder.CompleteInput();
            Assert.Equal(CodecOutputStatus.EndOfStream, encoder.Receive().Status);
            encoder.Reset();
            Assert.Equal(CodecInputStatus.Accepted, encoder.TrySend(input));
            using MediaPacket reset = encoder.Receive().Value!;
            Assert.Equal(packet.Data.ToArray(), reset.Data.ToArray());
            encoder.Dispose();
            decoder.Dispose();
            input.Dispose();
            decoded.Dispose();
            packet.Dispose();
            reset.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        internal static VideoFormat Format(int width, int height) => new(width, height, VideoPixelFormat.Yuv420P8,
            new(ColorPrimaries.Bt709, TransferFunction.Bt709, MatrixCoefficients.Bt709, ColorRange.Limited, ChromaLocation.Left),
            new SampleAspectRatio(4, 3));

        internal static VideoFrame Frame(VideoFormat format, MediaBufferAllocator allocator, long index)
        {
            PlaneLayout[] layout = format.CreateTightLayout();
            using WritableMediaBuffer storage = allocator.Allocate(layout.Sum(plane => plane.Stride * plane.Height));
            for (int plane = 0; plane < 3; plane++)
            {
                for (int y = 0; y < layout[plane].Height; y++)
                {
                    for (int x = 0; x < layout[plane].RowBytes; x++)
                    {
                        storage.Memory.Span[layout[plane].Offset + y * layout[plane].Stride + x] =
                            (byte)(32 + (x * 2 + y * 3 + plane * 35 + index * 7) % 180);
                    }
                }
            }
            using MediaBuffer buffer = storage.Publish();
            return new(format, buffer, layout, new(index * 40, new(1, 1000)), new(40, new(1, 1000)));
        }
    }
}
