// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Mediana.Codecs;
using Mediana.Containers.Mp4;
using Mediana.Core.Metadata;
using Mediana.Core.Video;
using Mediana.Documents.Pdf;
using Mediana.Images.Heif;
using Mediana.Images.Tgs;
using Mediana.IO;

namespace Mediana.Tests
{
    public class DocumentImageProbeTests
    {
        [Fact]
        public async Task HeifPrimaryMetadataMatchesEveryIndependentTransformedReference()
        {
            string root = Path.Combine(AppContext.BaseDirectory, "Data", "Heif");
            using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "manifest.json")));
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new Mp4ReaderFactory())
                .AddImageDecoder(new HeicImageDecoder(SampleAspectRatio.Square)).Build();
            foreach (JsonElement entry in manifest.RootElement.EnumerateArray())
            {
                string name = entry.GetProperty("file").GetString()!;
                await using RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(root, name)));
                MediaInfo info = await engine.ProbeAsync(source);
                ImageInfo image = Assert.IsType<ImageInfo>(info.Image);
                Assert.Null(info.Container);
                Assert.Equal("heif", image.FormatId);
                Assert.Equal(entry.GetProperty("width").GetInt32(), image.Width);
                Assert.Equal(entry.GetProperty("height").GetInt32(), image.Height);
            }
        }

        [Theory]
        [InlineData(false, "lottie")]
        [InlineData(true, "tgs")]
        public async Task AnimationMetadataIncludesCompositionDurationWithoutRendering(bool compressed, string format)
        {
            byte[] json = Encoding.UTF8.GetBytes("{\"v\":\"5.7.4\",\"w\":800,\"h\":600,\"fr\":25,\"ip\":15,\"op\":65,\"layers\":[]}");
            byte[] input = json;
            if (compressed)
            {
                using MemoryStream memory = new();
                using (GZipStream gzip = new(memory, CompressionLevel.Fastest, leaveOpen: true))
                {
                    await gzip.WriteAsync(json);
                }
                input = memory.ToArray();
            }
            IImageInfoReader reader = new TgsImageDecoder(renderOptions: new() { MaximumSamples = 1 });
            ImageInfo image = await reader.ReadInfoAsync(input);
            Assert.Equal(format, image.FormatId);
            Assert.Equal(800, image.Width);
            Assert.Equal(600, image.Height);
            Assert.Equal(25, image.FrameRate);
            Assert.Equal(TimeSpan.FromSeconds(2).Ticks, image.Duration!.Value.Value);
            Assert.Null(image.PageCount);
        }

        [Theory]
        [InlineData("graphics.pdf")]
        [InlineData("report.pdf")]
        public async Task PdfMetadataUsesPagePointsRegardlessOfRenderingResolution(string name)
        {
            byte[] input = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Pdf", name));
            using MemoryStream memory = new(input);
            using PdfDocument document = await PdfReader.ReadAsync(memory);
            PdfPageInfo expected = document.GetPageInfo(1);
            IImageInfoReader reader = new PdfImageDecoder(renderOptions: new() { MaximumWidth = 1 });
            ImageInfo image = await reader.ReadInfoAsync(input);
            Assert.Equal("pdf", image.FormatId);
            Assert.Equal(ImageDimensionUnit.Points, image.DimensionUnit);
            Assert.Equal(expected.Width, image.Width);
            Assert.Equal(expected.Height, image.Height);
            Assert.Equal(document.PageCount, image.PageCount);
        }
    }
}
