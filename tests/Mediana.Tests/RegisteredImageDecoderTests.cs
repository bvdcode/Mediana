// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Codecs;
using Mediana.Codecs.Jbig2;
using Mediana.Codecs.Jpeg2000;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.Images.Heif;
using Mediana.IO;

namespace Mediana.Tests
{
    public class RegisteredImageDecoderTests
    {
        [Fact]
        public async Task RealRegisteredImagesMatchIndependentPixelsAndOutliveTheirSources() =>
            await ImageEngineTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data"));

        [Theory]
        [InlineData("Heif", "alpha-rotated.heic")]
        [InlineData("Jpeg2000", "RGBA-129x101.jp2")]
        [InlineData("Jbig2", "mmr-odd.jb2")]
        public async Task RecognitionAndInputReplayWorkWithOneByteSequentialReads(string folder, string file)
        {
            byte[] input = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", folder, file));
            await using ShortReadSource source = new(input);
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (VideoFrame frame = await ImageEngineTestInput.CreateEngine().DecodeImageAsync(source, allocator))
            {
                Assert.Equal(ImageEngineTestInput.Aspect, frame.Format.SampleAspectRatio);
                Assert.Equal(frame.Format.Width * frame.Format.Height * 4, allocator.AllocatedBytes);
                Assert.False(source.IsDisposed);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task IndependentSourcesCanShareOneEngineAndAllocatorConcurrently()
        {
            MediaEngine engine = ImageEngineTestInput.CreateEngine();
            MediaBufferAllocator allocator = new(4 * 1024 * 1024);
            (string Folder, string File)[] inputs = [("Heif", "alpha-rotated.heic"), ("Jpeg2000", "RGBA-129x101.jp2"), ("Jbig2", "mmr-odd.jb2")];
            Task<VideoFrame>[] tasks = Enumerable.Range(0, 12).Select(async index =>
            {
                (string folder, string file) = inputs[index % inputs.Length];
                await using RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Data", folder, file)));
                return await engine.DecodeImageAsync(source, allocator);
            }).ToArray();
            VideoFrame[] frames = await Task.WhenAll(tasks);
            try
            {
                Assert.Equal(frames.Sum(frame => (long)frame.Format.Width * frame.Format.Height * 4), allocator.AllocatedBytes);
                foreach (VideoFrame frame in frames)
                {
                    Assert.Equal(ImageEngineTestInput.Aspect, frame.Format.SampleAspectRatio);
                    Assert.False(frame.Data.IsEmpty);
                }
            }
            finally
            {
                foreach (VideoFrame frame in frames)
                {
                    frame.Dispose();
                }
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void RecognitionRemainsFormatSpecificAndPixelAspectIsExplicit()
        {
            IImageDecoder[] decoders = [new HeicImageDecoder(SampleAspectRatio.Square),
                new Jpeg2000ImageDecoder(SampleAspectRatio.Square), new Jbig2ImageDecoder(SampleAspectRatio.Square)];
            foreach (IImageDecoder decoder in decoders)
            {
                Assert.False(decoder.CanDecode([]));
                Assert.False(decoder.CanDecode("not an image"u8));
            }
            Assert.Throws<ArgumentException>(() => new HeicImageDecoder(default));
            Assert.Throws<ArgumentException>(() => new Jpeg2000ImageDecoder(default));
            Assert.Throws<ArgumentException>(() => new Jbig2ImageDecoder(default));
            HeicImageDecoder heic = new(SampleAspectRatio.Square);
            Assert.True(heic.CanDecode([0, 0, 0, 20, .. "ftypheic"u8, 0, 0, 0, 0, .. "mif1"u8]));
            Assert.False(heic.CanDecode([0, 0, 0, 20, .. "ftypisom"u8, 0, 0, 0, 0, .. "mp42"u8]));
            byte[] extended = [0, 0, 0, 1, .. "ftyp"u8, .. new byte[8], .. "heic"u8, 0, 0, 0, 0];
            BinaryPrimitives.WriteUInt64BigEndian(extended.AsSpan(8), (ulong)extended.Length);
            Assert.True(heic.CanDecode(extended));
            Assert.False(heic.CanDecode(extended[..20]));
        }

        [Fact]
        public async Task ModuleLimitsUnsupportedSelectionsAndCancellationRemainEffective()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            byte[] input = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Jpeg2000", "RGB-3x5.jp2"));
            Jpeg2000ImageDecoder jpeg = new(SampleAspectRatio.Square, new Jpeg2000DecodeOptions { MaximumPixels = 1 });
            await Assert.ThrowsAsync<InvalidDataException>(async () => await jpeg.DecodeAsync(input, new(), allocator));
            await Assert.ThrowsAsync<NotSupportedException>(async () => await jpeg.DecodeAsync(input, new() { PageIndex = 1 }, allocator));
            await Assert.ThrowsAsync<NotSupportedException>(async () => await jpeg.DecodeAsync(input,
                new() { Time = new MediaTimestamp(1, new TimeBase(1, 1)) }, allocator));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await jpeg.DecodeAsync(input, new(), allocator, new CancellationToken(true)));
            byte[] jbig = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Jbig2", "mmr-odd.jb2"));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await new Jbig2ImageDecoder(SampleAspectRatio.Square)
                .DecodeAsync(jbig, new() { PageIndex = 1 }, allocator));
            byte[] heic = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Heif", "basic-8bit.heic"));
            await Assert.ThrowsAsync<InvalidDataException>(async () => await new HeicImageDecoder(SampleAspectRatio.Square, new HeicDecodeOptions(maxPixels: 1))
                .DecodeAsync(heic, new(), allocator));
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
