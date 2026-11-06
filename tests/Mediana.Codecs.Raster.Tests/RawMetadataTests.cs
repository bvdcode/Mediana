// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Tiff;
using Mediana.Core.Metadata;
using Mediana.Formats.Metadata;

namespace Mediana.Codecs.Raster.Tests
{
    public class RawMetadataTests
    {
        [Fact]
        public async Task SensorDirectoriesAreInspectedWithoutAnyEncodedSamples()
        {
            foreach (string family in new[] { "dng", "cr2", "nef", "arw", "tiff" })
            {
                byte[] encoded = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", $"raw-metadata-{family}.bin"));
                ImageInfo info = TiffMetadataReader.Read(encoded);
                Assert.Equal(family, info.FormatId);
                Assert.Equal(family == "tiff" ? 160 : 4000, info.Width);
                Assert.Equal(family == "tiff" ? 120 : 6000, info.Height);
                Assert.NotEmpty(info.Metadata.Children[0].Children);
                ImageInfo shared = await new TiffImageDecoder().ReadInfoAsync(encoded);
                Assert.Equal(info.FormatId, shared.FormatId); Assert.Equal(info.Width, shared.Width);
                Assert.ThrowsAny<OperationCanceledException>(() => TiffMetadataReader.Read(encoded, new(true)));
                Assert.Throws<InvalidDataException>(() => TiffMetadataReader.Read(encoded[..6]));
            }
        }
    }
}
