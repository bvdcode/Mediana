// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Codecs.H264;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.IO;

namespace Mediana.Tests
{
    internal static class H264EncodingTestInput
    {
        public static async Task VerifyAsync(string root, string name, int qp, int expectedCount)
        {
            byte[] expected = await File.ReadAllBytesAsync(Path.Combine(root, name + ".encoded-qp" + qp + ".yuv"));
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new Mp4ReaderFactory())
                .AddVideoDecoder(new H264DecoderFactory()).Build();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(root, name + ".mp4")));
            await using VideoSession session = await engine.OpenVideoAsync(source, 0);
            MediaBufferAllocator allocator = new(4 * 1024 * 1024);
            using VideoFrame first = await session.ReadFrameAsync() ?? throw new InvalidDataException("The encoder reference has no pictures.");
            using H264Encoder encoder = H264Encoder.Create(first.Format, session.Stream.TimeBase, allocator, new() { Quantizer = qp });
            using IMediaCodec<MediaPacket, VideoFrame> decoder = new H264DecoderFactory().Create(encoder.Stream, allocator);
            int offset = VerifyFrame(first, encoder, decoder, expected, 0);
            int count = 1;
            while (await session.ReadFrameAsync() is VideoFrame input)
            {
                using (input)
                {
                    offset = VerifyFrame(input, encoder, decoder, expected, offset);
                    count++;
                }
            }
            encoder.CompleteInput();
            decoder.CompleteInput();
            if (count != expectedCount || offset != expected.Length ||
                encoder.Receive().Status != CodecOutputStatus.EndOfStream || decoder.Receive().Status != CodecOutputStatus.EndOfStream)
            {
                throw new InvalidDataException("The encoder reference picture count or completed state differs.");
            }
            encoder.Dispose();
            decoder.Dispose();
            first.Dispose();
            await session.DisposeAsync();
            if (allocator.AllocatedBytes != 0 || session.AllocatedBufferBytes != 0)
            {
                throw new InvalidOperationException("Video encoding retained media buffers after disposal.");
            }
        }

        internal static int VerifyFrame(VideoFrame input, H264Encoder encoder, IMediaCodec<MediaPacket, VideoFrame> decoder,
            byte[] expected, int offset)
        {
            if (encoder.TrySend(input) != CodecInputStatus.Accepted)
            {
                throw new InvalidOperationException("The reference encoder did not accept its input.");
            }
            using MediaPacket packet = encoder.Receive().Value ?? throw new InvalidDataException("The encoder produced no packet.");
            if (!new H264RandomAccessInspector(encoder.Stream).IsIdr(packet) || packet.PresentationTime != input.Timestamp ||
                packet.DecodeTime != input.Timestamp || packet.Duration != input.Duration || packet.IsKeyFrame != true)
            {
                throw new InvalidDataException("The encoded packet changed input timing or lacks independent restart headers.");
            }
            decoder.Reset();
            decoder.TrySend(packet);
            decoder.CompleteInput();
            using VideoFrame output = decoder.Receive().Value ?? throw new InvalidDataException("The encoded packet produced no picture.");
            int bytes = input.Format.Width * input.Format.Height * 3 / 2;
            H264TestInput.VerifyFrame(output, expected.AsSpan(offset, bytes));
            if (output.Format.Color != input.Format.Color || output.Format.SampleAspectRatio != input.Format.SampleAspectRatio)
            {
                throw new InvalidDataException("The encoded video changed its color metadata or sample aspect ratio.");
            }
            return offset + bytes;
        }
    }
}
