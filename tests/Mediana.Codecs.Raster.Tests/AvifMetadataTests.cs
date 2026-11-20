// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Core.Metadata;
using Mediana.Images.Heif;

namespace Mediana.Codecs.Raster.Tests
{
    public class AvifMetadataTests
    {
        [Theory]
        [InlineData("metadata-reference.avif", 37, 29)]
        [InlineData("metadata-oriented-thumbnail.avif", 29, 37)]
        public async Task AvifUsesContainerTransformsAndExifWithoutDecodingPixels(string name, int width, int height)
        {
            byte[] file = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", name));
            AvifImageInspector reader = new(new(maxPixels: 1));
            Assert.True(reader.CanRead(file.AsSpan(0, 32)));
            ImageInfo info = await reader.ReadInfoAsync(file);
            Assert.Equal("avif", info.FormatId); Assert.Equal(width, info.Width); Assert.Equal(height, info.Height);
            MediaMetadata codec = info.Metadata.Children.Single(item => item.Scope == "av1/encoding/0");
            Assert.Equal("av1", codec.Properties.Single(item => item.Name == "codec").Values[0].AsText());
            Assert.Equal(8L, codec.Properties.Single(item => item.Name == "bit_depth").Values[0].AsInteger());
            if (name.Contains("oriented", StringComparison.Ordinal))
            {
                MediaMetadata exif = info.Metadata.Children.Single(item => item.Scope == "item/3").Children[0];
                Assert.Equal("Example Camera", exif.Children[0].Properties.Single(item => item.Name == "Make").Values[0].AsText());
                IReadOnlyList<EncodedImagePreview> previews = await reader.ReadPreviewsAsync(file);
                EncodedImagePreview preview = Assert.Single(previews);
                Assert.Equal("av1", preview.Info.FormatId); Assert.Equal(7, preview.Info.Width); Assert.Equal(5, preview.Info.Height);
                Assert.NotEmpty(preview.Info.Metadata.Properties.Single(item => item.Name == "configuration").Values);
                int offset = file.AsSpan().IndexOf(preview.Encoded.Span);
                file[offset] ^= 1;
                Assert.Equal(file[offset], preview.Encoded.Span[0]);
            }
        }

        [Fact]
        public async Task AvifInspectionRejectsInvalidConfigurationAndBoundsAndHonorsCancellation()
        {
            byte[] file = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "metadata-reference.avif"));
            AvifImageInspector reader = new();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await reader.ReadInfoAsync(file, new(true)));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await reader.ReadPreviewsAsync(file, new(true)));
            await Assert.ThrowsAsync<InvalidDataException>(async () => await reader.ReadInfoAsync(file.AsMemory(0, file.Length - 1)));
            int config = file.AsSpan().IndexOf("av1C"u8) + 4;
            file[config] = 0;
            await Assert.ThrowsAsync<InvalidDataException>(async () => await reader.ReadInfoAsync(file));
            BinaryPrimitives.WriteUInt32BigEndian(file, 14);
            Assert.False(reader.CanRead(file));
        }
    }
}
