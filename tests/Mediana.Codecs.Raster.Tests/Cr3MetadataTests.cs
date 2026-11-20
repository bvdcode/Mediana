// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Core.Metadata;
using Mediana.Images.Metadata;

namespace Mediana.Codecs.Raster.Tests
{
    public class Cr3MetadataTests
    {
        [Fact]
        public async Task CanonSensorExifGpsAndEveryOriginalJpegHaveIndependentScopes()
        {
            byte[] file = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "metadata-sensor.cr3"));
            byte[] jpeg = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "metadata-preview.jpg"));
            Cr3MetadataReader reader = new();
            Assert.True(reader.CanRead(file));
            ImageInspection result = reader.Inspect(file);
            Assert.Equal("cr3", result.Info.FormatId); Assert.Equal(3024, result.Info.Width); Assert.Equal(4032, result.Info.Height);
            Assert.Equal(4000UL, result.Info.Metadata.Properties.Single(item => item.Name == "declared_image_width").Values[0].AsUnsignedInteger());
            Assert.Equal(3, result.Previews.Count);
            foreach (EncodedImagePreview preview in result.Previews)
            {
                Assert.Equal(7, preview.Info.Width); Assert.Equal(5, preview.Info.Height); Assert.Equal(jpeg, preview.Encoded.ToArray());
            }
            MediaMetadata gps = result.Info.Metadata.Children.Single(item => item.Scope == "CMT4").Children[0].Children[0];
            Assert.Equal("GPS", gps.Scope);
            Assert.Equal("N", gps.Properties.Single(item => item.Name == "GPSLatitudeRef").Values[0].AsText());
            MediaMetadata exif = result.Info.Metadata.Children.Single(item => item.Scope == "CMT2").Children[0].Children[0];
            Assert.Equal("2026:01:02 03:04:05", exif.Properties.Single(item => item.Name == "DateTimeOriginal").Values[0].AsText());
            file.AsSpan(file.Length - jpeg.Length).Fill(0);
            Assert.Equal(0, result.Previews[^1].Encoded.Span[0]);
        }

        [Fact]
        public async Task CanonInvalidPreviewAndIndexExtentsAreRejected()
        {
            byte[] original = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "metadata-sensor.cr3"));
            Cr3MetadataReader reader = new();
            Assert.Throws<OperationCanceledException>(() => reader.Inspect(original, new(true)));
            Assert.Throws<InvalidDataException>(() => reader.Inspect(original.AsMemory(0, original.Length - 1)));
            byte[] broken = original.ToArray();
            int thumb = broken.AsSpan().IndexOf("THMB"u8) + 4;
            BinaryPrimitives.WriteUInt32BigEndian(broken.AsSpan(thumb + 8), uint.MaxValue);
            Assert.Throws<InvalidDataException>(() => reader.Inspect(broken));
            broken = original.ToArray();
            int chunk = broken.AsSpan().IndexOf("co64"u8) + 4;
            BinaryPrimitives.WriteUInt64BigEndian(broken.AsSpan(chunk + 8), (ulong)broken.Length);
            Assert.Throws<InvalidDataException>(() => reader.Inspect(broken));
        }
    }
}
