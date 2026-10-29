// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Codecs.Jpeg2000;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Images.Heif;

namespace Mediana.Tests
{
    public class ImageFrameTests
    {
        private static string Root => Path.Combine(AppContext.BaseDirectory, "Data");

        [Fact]
        public async Task RasterFormatsProduceOwnedFramesAndCompletePngFiles()
        {
            await ImageFrameTestInput.VerifyAsync(Root);
        }

        [Fact]
        public async Task UnknownColorAndIccProfilesAreNotRelabeledAsSrgb()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            Jpeg2000Image raw = Jpeg2000Decoder.Decode(await File.ReadAllBytesAsync(Path.Combine(Root, "Jpeg2000", "raw.j2k")));
            Assert.Throws<NotSupportedException>(() => raw.ToVideoFrame(allocator, SampleAspectRatio.Square));
            byte[] jp2 = await File.ReadAllBytesAsync(Path.Combine(Root, "Jpeg2000", "RGB-3x5.jp2"));
            int colr = jp2.AsSpan().IndexOf("colr"u8);
            jp2[colr + 4] = 2;
            Jpeg2000Image profile = Jpeg2000Decoder.Decode(jp2);
            Assert.True(profile.HasIccProfile);
            Assert.Throws<NotSupportedException>(() => profile.ToVideoFrame(allocator, SampleAspectRatio.Square));
            byte[] heic = await File.ReadAllBytesAsync(Path.Combine(Root, "Heif", "basic-8bit.heic"));
            int nclx = heic.AsSpan().IndexOf("nclx"u8);
            Assert.True(nclx >= 0);
            "prof"u8.CopyTo(heic.AsSpan(nclx, 4));
            RgbaImage rgba = await HeicDocument.Load(heic).DecodePrimaryAsync();
            Assert.False(rgba.ColorInformation.IccProfile.IsEmpty);
            Assert.Throws<NotSupportedException>(() => rgba.ToVideoFrame(allocator, SampleAspectRatio.Square));
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task PixelBudgetsCancellationAndExplicitAspectArePreserved()
        {
            Jpeg2000Image image = Jpeg2000Decoder.Decode(await File.ReadAllBytesAsync(Path.Combine(Root, "Jpeg2000", "RGB-3x5.jp2")));
            MediaBufferAllocator insufficient = new(4);
            Assert.Throws<MediaMemoryLimitException>(() => image.ToVideoFrame(insufficient, SampleAspectRatio.Square));
            Assert.Throws<OperationCanceledException>(() => image.ToVideoFrame(insufficient, SampleAspectRatio.Square,
                cancellationToken: new CancellationToken(true)));
            Assert.Equal(0, insufficient.AllocatedBytes);
            MediaBufferAllocator allocator = new(60);
            using VideoFrame frame = image.ToVideoFrame(allocator, new SampleAspectRatio(2, 1));
            Assert.Equal(new SampleAspectRatio(2, 1), frame.Format.SampleAspectRatio);
            using VideoFrame retained = frame.Retain();
            frame.Dispose();
            Assert.Equal(60, retained.Data.Length);
            retained.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task SyccConversionUsesCenteredChroma()
        {
            byte[] input = await File.ReadAllBytesAsync(Path.Combine(Root, "Jpeg2000", "RGB-3x5.jp2"));
            int colr = input.AsSpan().IndexOf("colr"u8);
            BinaryPrimitives.WriteUInt32BigEndian(input.AsSpan(colr + 7, 4), 18);
            Jpeg2000Image image = Jpeg2000Decoder.Decode(input);
            MediaBufferAllocator allocator = new(60);
            using VideoFrame frame = image.ToVideoFrame(allocator, SampleAspectRatio.Square);
            for (int y = 0; y < image.Height; y++)
            {
                for (int x = 0; x < image.Width; x++)
                {
                    int luma = image.GetSample(x, y, 0);
                    int cb = image.GetSample(x, y, 1) - 128;
                    int cr = image.GetSample(x, y, 2) - 128;
                    byte[] expected = [(byte)Math.Clamp(Math.Round(luma + 1.402 * cr), 0, 255),
                        (byte)Math.Clamp(Math.Round(luma - .344136 * cb - .714136 * cr), 0, 255),
                        (byte)Math.Clamp(Math.Round(luma + 1.772 * cb), 0, 255), 255];
                    Assert.Equal(expected, frame.GetRow(0, y).Slice(x * 4, 4).ToArray());
                }
            }
        }
    }
}
