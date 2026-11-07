// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Codecs.Jpeg;
using Mediana.Containers.Matroska;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.Formats;
using Mediana.IO;

namespace Mediana.Tests
{
    public class MjpegDecoderTests
    {
        [Fact]
        public async Task EverySequentialAndProgressivePictureUsesTheSharedJpegDecoderAndOwnsOnlyItsOutput()
        {
            string root = Path.Combine(AppContext.BaseDirectory, "Data", "Jpeg");
            foreach (string file in Directory.EnumerateFiles(root, "*.jpg"))
            {
                byte[] bytes = await File.ReadAllBytesAsync(file);
                MediaBufferAllocator allocator = new(8 * 1024 * 1024);
                EncodedStreamInfo stream = Stream();
                using IMediaCodec<MediaPacket, VideoFrame> decoder = new MjpegDecoderFactory().Create(stream, allocator);
                using MediaPacket packet = Packet(bytes);
                Assert.True(new MjpegDecoderFactory().CreateRandomAccessInspector(stream).IsRandomAccess(packet));
                Assert.Equal(CodecInputStatus.Accepted, decoder.TrySend(packet));
                Assert.Equal(CodecInputStatus.OutputRequired, decoder.TrySend(packet));
                using VideoFrame frame = decoder.Receive().Value;
                Assert.Equal(packet.PresentationTime, frame.Timestamp);
                Assert.Equal(packet.Duration, frame.Duration);
                Assert.Equal(frame.Format.Width * frame.Format.Height * 4, allocator.AllocatedBytes);
                using JpegImage image = JpegDecoder.DecodeComponents(bytes, new(8 * 1024 * 1024));
                byte[] rgba = new byte[frame.Data.Length];
                JpegColorSpace color = image.AdobeColorTransform switch
                {
                    0 => JpegColorSpace.Rgb,
                    null or 1 => JpegColorSpace.YCbCr,
                    _ => throw new InvalidDataException("Unexpected JPEG corpus component transform.")
                };
                image.WriteRgbaTo(rgba, color);
                Assert.Equal(rgba, frame.Data.ToArray());
                decoder.CompleteInput();
                Assert.Equal(CodecOutputStatus.EndOfStream, decoder.Receive().Status);
                decoder.Dispose();
                Assert.Equal(rgba, frame.Data.ToArray());
                frame.Dispose();
                Assert.Equal(0, allocator.AllocatedBytes);
            }
        }

        [Fact]
        public void FailureResetCancellationAndHeaderInspectionKeepStorageAndStateConsistent()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            MjpegDecoderFactory factory = new();
            using IMediaCodec<MediaPacket, VideoFrame> decoder = factory.Create(Stream(), allocator);
            using MediaPacket bad = Packet([255, 216, 255, 217]);
            Assert.Throws<InvalidDataException>(() => factory.CreateRandomAccessInspector(Stream()).IsRandomAccess(bad));
            Assert.Throws<InvalidDataException>(() => decoder.TrySend(bad));
            Assert.Throws<InvalidOperationException>(() => decoder.Receive());
            Assert.Equal(0, allocator.AllocatedBytes);
            decoder.Reset();
            using MediaPacket valid = Packet(JpegTestData.Image());
            Assert.Equal(CodecInputStatus.Accepted, decoder.TrySend(valid));
            Assert.ThrowsAny<OperationCanceledException>(() => decoder.Receive(new CancellationToken(true)));
            decoder.Reset();
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.Equal(CodecInputStatus.Accepted, decoder.TrySend(valid));
            using VideoFrame retained = decoder.Receive().Value;
            decoder.CompleteInput();
            Assert.Throws<InvalidOperationException>(() => decoder.TrySend(valid));
            decoder.Reset();
            Assert.Equal(new byte[] { 128, 128, 128, 255 }, retained.Data.ToArray());
        }

        [Theory]
        [InlineData("420")]
        [InlineData("422")]
        [InlineData("444")]
        public async Task MatroskaMjpegDecodesEveryPictureWithIndependentTimingAndPixelReferences(string sampling)
        {
            string root = Path.Combine(AppContext.BaseDirectory, "Data", "Mjpeg");
            byte[] expected = await File.ReadAllBytesAsync(Path.Combine(root, $"mjpeg-{sampling}.rgba"));
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new MatroskaReaderFactory())
                .AddVideoDecoder(new MjpegDecoderFactory()).Build();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(root, $"mjpeg-{sampling}.mkv")));
            await using VideoSession video = await engine.OpenVideoAsync(source);
            Assert.Equal("mjpeg", video.Stream.CodecId);
            Assert.Equal(VideoDisplayMatrix.Identity, video.Stream.DisplayMatrix);
            long squares = 0;
            int maximum = 0, index = 0;
            while (await video.ReadFrameAsync() is VideoFrame frame)
            {
                using (frame)
                {
                    Assert.Equal(new MediaTimestamp(index, new(1, 8)), frame.Timestamp);
                    Assert.Equal(new MediaDuration(125000000, new(1, 1000000000)), frame.Duration);
                    Assert.Equal(new SampleAspectRatio(1, 1), frame.Format.SampleAspectRatio);
                    Assert.Equal(64 * 48 * 4, frame.Data.Length);
                    ReadOnlySpan<byte> reference = expected.AsSpan(index * frame.Data.Length, frame.Data.Length);
                    ReadOnlySpan<byte> actual = frame.Data.Span;
                    for (int sample = 0; sample < actual.Length; sample++)
                    {
                        int difference = Math.Abs(reference[sample] - actual[sample]);
                        maximum = Math.Max(maximum, difference);
                        squares += difference * difference;
                        if ((sample & 3) == 3) { Assert.Equal(255, actual[sample]); }
                    }
                }
                index++;
            }
            Assert.Equal(12, index);
            Assert.True(maximum <= 4, $"Maximum RGB difference {maximum}.");
            double mse = squares / (12d * 64 * 48 * 3);
            Assert.True(mse <= 1, $"RGB mean square error {mse}.");
            await video.DisposeAsync();
            Assert.Equal(0, video.AllocatedBufferBytes);
        }

        private static EncodedStreamInfo Stream() => new(7, MediaKind.Video, "mjpeg", StreamFormats.Mjpeg, new(1, 1000),
            displayMatrix: VideoDisplayMatrix.Identity, aspectRatioDescription: new(new(1, 1)));

        private static MediaPacket Packet(byte[] bytes)
        {
            MediaBufferAllocator allocator = new(bytes.Length);
            using WritableMediaBuffer writable = allocator.Allocate(bytes.Length);
            bytes.CopyTo(writable.Memory);
            using MediaBuffer buffer = writable.Publish();
            return new(7, buffer, new(321, new(1, 1000)), new(321, new(1, 1000)), new(40, new(1, 1000)));
        }
    }
}
