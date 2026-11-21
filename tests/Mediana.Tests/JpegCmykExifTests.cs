// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Codecs;
using Mediana.Codecs.Jpeg;
using Mediana.Core.Memory;
using Mediana.Core.Metadata;
using Mediana.Core.Video;
using Mediana.Imaging;
using Mediana.Processing;

namespace Mediana.Tests
{
    public class JpegCmykExifTests
    {
        [Theory]
        [InlineData("cmyk-adobe")]
        [InlineData("cmyk-plain")]
        [InlineData("cmyk-progressive")]
        [InlineData("ycck-multiscan")]
        public async Task SourceExifDoesNotRelabelReconstructedDeviceRgb(string name)
        {
            string root = Path.Combine(AppContext.BaseDirectory, "Data", "Raster");
            byte[] original = await File.ReadAllBytesAsync(Path.Combine(root, name + ".jpg"));
            byte[] reference = await File.ReadAllBytesAsync(Path.Combine(root, name + ".rgba"));
            JpegImageDecoder decoder = new();
            MediaBufferAllocator allocator = new(1024 * 1024);
            foreach (ushort colorSpace in new ushort[] { 1, 65535 })
            {
                byte[] encoded = JpegTestData.Insert(original, 225, ExifColor(colorSpace));
                ImageInfo info = await decoder.ReadInfoAsync(encoded);
                MediaMetadata exif = info.Metadata.Children.Single(item => item.Scope == "Exif").Children.Single(item => item.Scope == "Exif");
                Assert.Equal((ulong)colorSpace, exif.Properties.Single(property => property.Name == "ColorSpace").Values[0].AsUnsignedInteger());
                using (VideoFrame frame = await decoder.DecodeAsync(encoded, new(), allocator))
                {
                    Assert.Null(frame.Format.ColorDescription);
                    Assert.Equal(ColorPrimaries.Unspecified, frame.Format.GetColorInterpretation().Primaries);
                    Assert.Equal(TransferFunction.Unspecified, frame.Format.Color.Transfer);
                    Assert.Throws<NotSupportedException>(() => SrgbConverter.Apply(frame, allocator));
                    using VideoFrame control = await decoder.DecodeAsync(original, new(), allocator);
                    Assert.Equal(control.Data.ToArray(), frame.Data.ToArray());
                }
                List<string> warnings = [];
                using (VideoFrame preview = await ImageResources.DecodeAsync(encoded, new ImageDecodeContext([decoder]),
                    new() { WarningHandler = warnings.Add }, allocator, SdrPreviewColorPolicy.Resolve))
                {
                    Assert.Equal(VideoColorInfo.Srgb, preview.Format.Color);
                    Assert.Single(warnings);
                    Assert.Equal(reference.Length, preview.Data.Length);
                    for (int index = 0; index < reference.Length; index++)
                    {
                        Assert.InRange(Math.Abs(reference[index] - preview.Data.Span[index]), 0, (index & 3) == 3 ? 0 : 4);
                    }
                    Assert.Equal(reference.Length, allocator.AllocatedBytes);
                }
                Assert.Equal(0, allocator.AllocatedBytes);
            }
        }

        [Fact]
        public async Task FourComponentIccIsNotDiscardedByDeviceConversion()
        {
            byte[] input = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Raster", "cmyk-adobe.jpg"));
            byte[] exif = JpegTestData.Insert(input, 225, ExifColor(65535));
            byte[] profiled = JpegTestData.Insert(exif, 226, [.. "ICC_PROFILE\0"u8, 1, 1, 0]);
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (VideoFrame decoded = await new JpegImageDecoder().DecodeAsync(profiled, new(), allocator))
            {
                Assert.Equal(VideoPixelFormat.Cmyk32, decoded.Format.PixelFormat);
                Assert.Equal(new byte[] { 0 }, Assert.IsType<IccColorDescription>(decoded.Format.ColorDescription).Profile.ToArray());
                Assert.Throws<InvalidDataException>(() => SrgbConverter.ApplyWithColorPolicy(decoded,
                    _ => throw new InvalidOperationException("Invalid ICC must not invoke the color-label policy."), allocator));
                Assert.Equal(decoded.Data.Length, allocator.AllocatedBytes);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        private static byte[] ExifColor(ushort colorSpace)
        {
            byte[] tiff = [73, 73, 42, 0, 8, 0, 0, 0,
                1, 0, 105, 135, 4, 0, 1, 0, 0, 0, 26, 0, 0, 0, 0, 0, 0, 0,
                1, 0, 1, 160, 3, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
            BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(36), colorSpace);
            return [.. "Exif\0\0"u8, .. tiff];
        }
    }
}
