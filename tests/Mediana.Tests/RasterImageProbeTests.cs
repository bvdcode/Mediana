// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Text.Json;
using Mediana.Codecs;
using Mediana.Codecs.Jbig2;
using Mediana.Codecs.Jpeg2000;
using Mediana.Core.Metadata;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    public class RasterImageProbeTests
    {
        [Theory]
        [InlineData("Jpeg2000")]
        [InlineData("Jbig2")]
        public async Task RasterMetadataMatchesIndependentReferenceDimensions(string folder)
        {
            string root = Path.Combine(AppContext.BaseDirectory, "Data", folder);
            using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "manifest.json")));
            IImageInfoReader reader = folder switch
            {
                "Jpeg2000" => new Jpeg2000ImageDecoder(SampleAspectRatio.Square),
                "Jbig2" => new Jbig2ImageDecoder(SampleAspectRatio.Square),
                _ => throw new ArgumentOutOfRangeException(nameof(folder))
            };
            foreach (JsonElement entry in manifest.RootElement.EnumerateArray())
            {
                string name = entry.GetProperty("name").GetString()!;
                string file = folder switch
                {
                    "Jpeg2000" => entry.GetProperty("file").GetString()!,
                    "Jbig2" => name + ".jb2",
                    _ => throw new ArgumentOutOfRangeException(nameof(folder))
                };
                ImageInfo info = await reader.ReadInfoAsync(await File.ReadAllBytesAsync(Path.Combine(root, file)));
                Assert.Equal(entry.GetProperty("width").GetInt32(), info.Width);
                Assert.Equal(entry.GetProperty("height").GetInt32(), info.Height);
                Assert.Equal(ImageDimensionUnit.Pixels, info.DimensionUnit);
            }
        }

        [Fact]
        public async Task Jpeg2000ProbeReadsRawSizeHeaderWithoutTilePacketsOrSampleBuffers()
        {
            byte[] encoded = await File.ReadAllBytesAsync(Data("Jpeg2000", "RGB-129x101.jp2"));
            int start = encoded.AsSpan().IndexOf(new byte[] { 0xff, 0x4f, 0xff, 0x51 });
            Assert.True(start >= 0);
            int size = BinaryPrimitives.ReadUInt16BigEndian(encoded.AsSpan(start + 4));
            byte[] header = encoded.AsSpan(start, 4 + size).ToArray();
            IImageInfoReader reader = new Jpeg2000ImageDecoder(SampleAspectRatio.Square, new() { MaximumWorkingBytes = 1024 });
            ImageInfo info = await reader.ReadInfoAsync(header);
            Assert.Equal(129, info.Width);
            Assert.Equal(101, info.Height);
            Assert.Throws<InvalidDataException>(() => Jpeg2000Decoder.Decode(header));
        }

        [Fact]
        public async Task Jbig2ProbeSkipsRegionDataAndCountsPagesWithoutPixelStorage()
        {
            byte[] encoded = await File.ReadAllBytesAsync(Data("Jbig2", "mmr-white.jb2"));
            byte[] page = Jbig2TestInput.Parts(encoded)[0];
            byte[] input = [.. encoded.AsSpan(0, 8), 3,
                .. Jbig2TestInput.Segment(1, 48, 1, page),
                .. Jbig2TestInput.Segment(2, 38, 1, []),
                .. Jbig2TestInput.Segment(3, 48, 300, page),
                .. Jbig2TestInput.Segment(4, 51, 0, [])];
            IImageInfoReader reader = new Jbig2ImageDecoder(SampleAspectRatio.Square, new() { MaximumWorkingBytes = 1024 });
            ImageInfo info = await reader.ReadInfoAsync(input);
            Assert.Equal(2, info.PageCount);
            Assert.Equal(BinaryPrimitives.ReadUInt32BigEndian(page), info.Width);
            Assert.Equal(BinaryPrimitives.ReadUInt32BigEndian(page.AsSpan(4)), info.Height);
            Assert.Throws<InvalidDataException>(() => Jbig2Decoder.Decode(input));
        }

        [Fact]
        public async Task Jbig2ProbeReadsRandomAccessSegmentOrganization()
        {
            byte[] encoded = await File.ReadAllBytesAsync(Data("Jbig2", "mmr-white.jb2"));
            byte[] page = Jbig2TestInput.Parts(encoded)[0];
            byte[] segment = Jbig2TestInput.Segment(1, 48, 1, page);
            byte[] input = [.. encoded.AsSpan(0, 8), 2, .. segment.AsSpan(0, 14),
                .. Jbig2TestInput.Segment(2, 51, 0, []), .. page];
            ImageInfo info = await new Jbig2ImageDecoder(SampleAspectRatio.Square).ReadInfoAsync(input);
            Assert.Equal(1, info.PageCount);
            Assert.Equal(BinaryPrimitives.ReadUInt32BigEndian(page), info.Width);
        }

        private static string Data(string folder, string file) => Path.Combine(AppContext.BaseDirectory, "Data", folder, file);
    }
}
