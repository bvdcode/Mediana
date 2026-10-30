// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Codecs.H264;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.IO;
using Mediana.Processing;

namespace Mediana.Tests
{
    internal static class YuvScalingTestInput
    {
        public static async Task VerifySyntheticAsync(string root)
        {
            MediaBufferAllocator allocator = new(65536);
            byte[] source = await File.ReadAllBytesAsync(Path.Combine(root, "area-10x8.input.yuv"));
            using WritableMediaBuffer storage = allocator.Allocate(source.Length);
            source.CopyTo(storage.Memory);
            using MediaBuffer buffer = storage.Publish();
            foreach (ChromaLocation location in new[] { ChromaLocation.Left, ChromaLocation.Center, ChromaLocation.TopLeft,
                ChromaLocation.Top, ChromaLocation.BottomLeft, ChromaLocation.Bottom })
            {
                VideoFormat format = new(10, 8, VideoPixelFormat.Yuv420P8,
                    new(ColorPrimaries.Bt709, TransferFunction.Bt709, MatrixCoefficients.Bt709, ColorRange.Limited, location), new(4, 3));
                using VideoFrame input = new(format, buffer, format.CreateTightLayout(), new(77, new(1, 1000)), new(40, new(1, 1000)));
                using Yuv420AreaResizer resizer = Yuv420AreaResizer.Create(format, 6, 4, allocator);
                using VideoFrame output = resizer.Apply(input);
                byte[] expected = await File.ReadAllBytesAsync(Path.Combine(root, "area-6x4-" + location + ".yuv"));
                H264TestInput.VerifyFrame(output, expected);
                if (output.Format.SampleAspectRatio != new SampleAspectRatio(10, 9) || output.Timestamp != input.Timestamp ||
                    output.Duration != input.Duration || output.Format.Color != input.Format.Color)
                {
                    throw new InvalidDataException("Video scaling changed display aspect, timing or color metadata.");
                }
            }
            buffer.Dispose();
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidOperationException("Synthetic YUV scaling retained media storage.");
            }
        }

        public static async Task VerifySequenceAsync(string root, string name, int width, int height, int count, SampleAspectRatio expectedAspect)
        {
            byte[] expected = await File.ReadAllBytesAsync(Path.Combine(root, name + ".scaled-" + width + "x" + height + ".yuv"));
            byte[] encoded = await File.ReadAllBytesAsync(Path.Combine(root, name + ".scaled-" + width + "x" + height + ".encoded-qp26.yuv"));
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new Mp4ReaderFactory()).AddVideoDecoder(new H264DecoderFactory()).Build();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(root, name + ".mp4")));
            await using VideoSession session = await engine.OpenVideoAsync(source, 0);
            MediaBufferAllocator allocator = new(1024 * 1024);
            using VideoFrame first = await session.ReadFrameAsync() ?? throw new InvalidDataException("The scaling input has no pictures.");
            using Yuv420AreaResizer resizer = Yuv420AreaResizer.CreateFit(first.Format, width, first.Format.Height, allocator);
            if (resizer.OutputFormat.Width != width || resizer.OutputFormat.Height != height || resizer.OutputFormat.SampleAspectRatio != expectedAspect)
            {
                throw new InvalidDataException("The scaling plan changed the expected dimensions or display aspect.");
            }
            using VideoFrame retained = resizer.Apply(first);
            using H264Encoder encoder = H264Encoder.Create(resizer.OutputFormat, session.Stream.TimeBase, allocator);
            using IMediaCodec<MediaPacket, VideoFrame> decoder = new H264DecoderFactory().Create(encoder.Stream, allocator);
            int encodedOffset = H264EncodingTestInput.VerifyFrame(retained, encoder, decoder, encoded, 0);
            int frameBytes = width * height * 3 / 2;
            H264TestInput.VerifyFrame(retained, expected.AsSpan(0, frameBytes));
            int index = 1;
            while (await session.ReadFrameAsync() is VideoFrame input)
            {
                using (input)
                using (VideoFrame output = resizer.Apply(input))
                {
                    H264TestInput.VerifyFrame(output, expected.AsSpan(index * frameBytes, frameBytes));
                    encodedOffset = H264EncodingTestInput.VerifyFrame(output, encoder, decoder, encoded, encodedOffset);
                    if (output.Timestamp != input.Timestamp || output.Duration != input.Duration)
                    {
                        throw new InvalidDataException("Video scaling changed input frame timing.");
                    }
                    index++;
                }
            }
            encoder.CompleteInput();
            decoder.CompleteInput();
            if (encodedOffset != encoded.Length || encoder.Receive().Status != CodecOutputStatus.EndOfStream ||
                decoder.Receive().Status != CodecOutputStatus.EndOfStream)
            {
                throw new InvalidDataException("The scaled encoding reference count or drained state differs.");
            }
            encoder.Dispose();
            decoder.Dispose();
            resizer.Dispose();
            first.Dispose();
            await session.DisposeAsync();
            H264TestInput.VerifyFrame(retained, expected.AsSpan(0, frameBytes));
            retained.Dispose();
            if (index != count || expected.Length != count * frameBytes || allocator.AllocatedBytes != 0 || session.AllocatedBufferBytes != 0)
            {
                throw new InvalidDataException("The complete scaling reference count or released storage differs.");
            }
        }
    }
}
