// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Codecs.Jpeg;
using Mediana.Core.Metadata;
using Mediana.Images.Metadata;

namespace Mediana.Codecs.Raster.Tests
{
    public class ImageMetadataReaderTests
    {
        [Fact]
        public async Task PcxMetadataDoesNotDecodePixelsAndRetainsDeclaredPaletteAndResolution()
        {
            byte[] file = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "metadata-reference.pcx"));
            ImageInspection inspection = new PcxMetadataReader().Inspect(file);
            Assert.Equal(37, inspection.Info.Width); Assert.Equal(29, inspection.Info.Height);
            Assert.Empty(inspection.Previews);
            Assert.Equal(3L, inspection.Info.Metadata.Properties.Single(item => item.Name == "planes").Values[0].AsInteger());
            file.AsSpan(128).Fill(255);
            Assert.Equal(37, new PcxMetadataReader().Inspect(file).Info.Width);
            for (int length = 0; length < 128; length++)
            {
                Assert.Throws<InvalidDataException>(() => new PcxMetadataReader().Inspect(file.AsMemory(0, length)));
            }
            file[66] = 0; file[67] = 0;
            Assert.Throws<InvalidDataException>(() => new PcxMetadataReader().Inspect(file));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public async Task PsdAndPsbExposeOwnedMetadataAndUnchangedBorrowedJpeg(int version)
        {
            byte[] file = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", version == 1 ? "metadata-reference.psd" : "metadata-reference.psb"));
            byte[] jpeg = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "metadata-preview.jpg"));
            PsdMetadataReader reader = new();
            ImageInspection inspection = reader.Inspect(file);
            Assert.Equal(version == 1 ? "psd" : "psb", inspection.Info.FormatId);
            Assert.Equal(37, inspection.Info.Width); Assert.Equal(29, inspection.Info.Height);
            Assert.Equal(jpeg, Assert.Single(inspection.Previews).Encoded.ToArray());
            MediaMetadata directory = inspection.Info.Metadata.Children[0].Children[0].Children[0];
            Assert.Equal("Example Camera", directory.Properties.Single(item => item.Name == "Make").Values[0].AsText());
            Assert.Equal(6UL, directory.Properties.Single(item => item.Name == "Orientation").Values[0].AsUnsignedInteger());
            Assert.NotEmpty(inspection.Info.Metadata.Children.Single(item => item.Scope == "preview/0").Properties);
            ImageInfo preview = await new JpegImageDecoder().ReadInfoAsync(inspection.Previews[0].Encoded);
            Assert.Equal(7, preview.Width); Assert.Equal(5, preview.Height);
            foreach (int length in new[] { 0, 5, 25, 29, file.Length - 37 * 29 * 3 - 1 })
            {
                Assert.Throws<InvalidDataException>(() => reader.Inspect(file.AsMemory(0, length)));
            }
            int jpegOffset = file.AsSpan().IndexOf(jpeg);
            file[jpegOffset + jpeg.Length - 1] ^= 1;
            Assert.NotEqual(jpeg[^1], inspection.Previews[0].Encoded.Span[^1]);
            Assert.Equal(7, preview.Width);
            BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(26), uint.MaxValue);
            Assert.Throws<InvalidDataException>(() => reader.Inspect(file));
        }

        [Fact]
        public void PreCanceledMetadataInspectionDoesNoParsing()
        {
            foreach (ImageMetadataReader reader in new ImageMetadataReader[] { new PcxMetadataReader(), new PsdMetadataReader() })
            {
                Assert.Throws<OperationCanceledException>(() => reader.Inspect(ReadOnlyMemory<byte>.Empty, new(true)));
            }
        }
    }
}
