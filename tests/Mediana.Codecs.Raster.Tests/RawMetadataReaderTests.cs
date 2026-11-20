// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Codecs;
using Mediana.Core.Metadata;
using Mediana.Images.Metadata;

namespace Mediana.Codecs.Raster.Tests
{
    public class RawMetadataReaderTests
    {
        [Theory]
        [InlineData("nrw")]
        [InlineData("orf")]
        [InlineData("rw2")]
        [InlineData("pef")]
        [InlineData("srw")]
        public async Task CameraSensorAndOriginalJpegRemainDistinctInEitherByteOrder(string family)
        {
            byte[] jpeg = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "metadata-preview.jpg"));
            foreach (string order in new[] { "le", "be" })
            {
                byte[] file = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", $"metadata-sensor-{family}-{order}.{family}"));
                TiffImageMetadataReader reader = new();
                Assert.True(reader.CanRead(file.AsSpan(0, 4)));
                ImageInspection result = reader.Inspect(file);
                Assert.Equal(family, result.Info.FormatId);
                Assert.Equal(3024, result.Info.Width); Assert.Equal(4032, result.Info.Height);
                Assert.Equal(4032UL, result.Info.Metadata.Properties.Single(item => item.Name == "sensor_width").Values[0].AsUnsignedInteger());
                EncodedImagePreview preview = Assert.Single(result.Previews);
                Assert.Equal(7, preview.Info.Width); Assert.Equal(5, preview.Info.Height); Assert.Equal(jpeg, preview.Encoded.ToArray());
                MediaMetadata exif = result.Info.Metadata.Children[0].Children.Single(item => item.Scope == "Exif");
                Assert.Equal("2026:01:02 03:04:05", exif.Properties.Single(item => item.Name == "DateTimeOriginal").Values[0].AsText());
                int start = file.AsSpan().IndexOf(jpeg);
                file[start] = 0;
                Assert.Equal(0, preview.Encoded.Span[0]);
                Assert.Equal(3024, result.Info.Width);
                Assert.Throws<InvalidDataException>(() => reader.Inspect(file));
            }
        }

        [Theory]
        [InlineData("nikon")]
        [InlineData("olympus")]
        [InlineData("pentax")]
        public async Task MakerNotePreviewsUseVendorOffsetOrigins(string maker)
        {
            byte[] file = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", $"metadata-maker-{maker}.raw"));
            byte[] jpeg = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "metadata-preview.jpg"));
            ImageInspection result = new TiffImageMetadataReader().Inspect(file);
            Assert.Equal(4032, result.Info.Width); Assert.Equal(3024, result.Info.Height);
            Assert.Equal(jpeg, Assert.Single(result.Previews).Encoded.ToArray());
        }

        [Fact]
        public async Task RafReadsFullSensorCropAndBorrowedJpegWithoutSensorSamples()
        {
            byte[] file = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "metadata-sensor.raf"));
            RafMetadataReader reader = new();
            ImageInspection result = reader.Inspect(file);
            Assert.Equal("raf", result.Info.FormatId); Assert.Equal(4032, result.Info.Width); Assert.Equal(3024, result.Info.Height);
            Assert.Equal(4016UL, result.Info.Metadata.Properties.Single(item => item.Name == "crop_width").Values[0].AsUnsignedInteger());
            EncodedImagePreview preview = Assert.Single(result.Previews);
            byte[] jpeg = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "metadata-preview.jpg"));
            Assert.Equal(jpeg, preview.Encoded.ToArray());
            file[160] = 0;
            Assert.Equal(0, preview.Encoded.Span[0]);
        }

        [Fact]
        public async Task RawInvalidOffsetsCyclesAndCancellationFailBeforePixelDecode()
        {
            foreach (ImageMetadataReader reader in new ImageMetadataReader[] { new TiffImageMetadataReader(), new RafMetadataReader() })
            { Assert.Throws<OperationCanceledException>(() => reader.Inspect(ReadOnlyMemory<byte>.Empty, new(true))); }
            byte[] tiff = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "metadata-sensor-pef-le.pef"));
            BinaryPrimitives.WriteUInt32LittleEndian(tiff.AsSpan(4), uint.MaxValue);
            Assert.Throws<InvalidDataException>(() => new TiffImageMetadataReader().Inspect(tiff));
            byte[] raf = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "metadata-sensor.raf"));
            BinaryPrimitives.WriteUInt32BigEndian(raf.AsSpan(88), uint.MaxValue);
            Assert.Throws<InvalidDataException>(() => new RafMetadataReader().Inspect(raf));
            byte[] rw2 = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "metadata-sensor-rw2-le.rw2"));
            rw2[8] = 0;
            Assert.Throws<InvalidDataException>(() => new TiffImageMetadataReader().Inspect(rw2));
        }
    }
}
