// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Codecs;
using Mediana.Codecs.Jpeg;
using Mediana.Codecs.Png;
using Mediana.Core.Metadata;
using Mediana.Core.Video;
using Mediana.Images.Heif;
using Mediana.IO;

namespace Mediana.Tests
{
    [Collection("CommandLine")]
    public class MediaMetadataPipelineTests : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("mediana-probe-properties-").FullName;

        [Theory]
        [InlineData("heif")]
        [InlineData("jpeg")]
        [InlineData("png")]
        public async Task SharedExifValuesSurviveSourceDisposalAndUseTheSameCliOutput(string format)
        {
            byte[] exif = ExifMetadataTestInput.Create(false);
            byte[] encoded;
            IImageDecoder decoder;
            switch (format)
            {
                case "heif":
                    encoded = await HeifExifTestInput.CreateAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Heif"), false);
                    decoder = new HeicImageDecoder(SampleAspectRatio.Square, new(maxPixels: 1));
                    break;
                case "jpeg":
                    byte[] original = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Jpeg", "rgb-s0-1x1.jpg"));
                    byte[] segment = new byte[exif.Length + 10];
                    segment[0] = 255; segment[1] = 225;
                    BinaryPrimitives.WriteUInt16BigEndian(segment.AsSpan(2), (ushort)(exif.Length + 8));
                    "Exif\0\0"u8.CopyTo(segment.AsSpan(4)); exif.CopyTo(segment, 10);
                    encoded = [.. original.AsSpan(0, 2), .. segment, .. original.AsSpan(2)];
                    decoder = new JpegImageDecoder();
                    break;
                case "png":
                    encoded = PngDecodeTestData.Image([0, 1, 2, 3, 255], PngDecodeTestData.Chunk("eXIf", exif));
                    decoder = new PngImageDecoder();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(format));
            }
            MediaEngine engine = new MediaEngineBuilder().AddImageDecoder(decoder).Build();
            MediaInfo info;
            await using (RandomAccessStreamByteSource source = new(new MemoryStream(encoded)))
            {
                info = await engine.ProbeAsync(source);
            }
            MediaMetadata metadata = info.Metadata;
            List<MediaProperty> properties = Flatten(metadata).ToList();
            Assert.Equal("Камера", properties.Single(property => property.Name == "Make").Values[0].AsText());
            Assert.Equal((1L, 125L), properties.Single(property => property.Name == "ExposureTime").Values[0].AsRational());
            string path = Path.Combine(directory, "input.bin");
            await File.WriteAllBytesAsync(path, encoded);
            (int code, string output, string error) = await CliTests.RunAsync(["probe", path]);
            Assert.True(code == 0, error);
            Assert.Contains("\"exif\"/\"Make\": \"Камера\"", output, StringComparison.Ordinal);
            Assert.Contains("\"exif\"/\"ExposureTime\": 1/125", output, StringComparison.Ordinal);
            Assert.Contains("Encoded size:", output, StringComparison.Ordinal);
            Array.Clear(encoded);
            Assert.Equal("Камера", properties.Single(property => property.Name == "Make").Values[0].AsText());
            Assert.Equal(new byte[] { 9, 8, 7 }, properties.Single(property => property.Name == "MakerNote").Values[0].AsBytes().ToArray());
            Assert.Single(Directory.GetFiles(directory));
        }

        private static IEnumerable<MediaProperty> Flatten(MediaMetadata metadata) => metadata.Properties.Concat(metadata.Children.SelectMany(Flatten));

        [Theory]
        [InlineData("svg", "\"svg\"/\"viewBox\"")]
        [InlineData("pdf", "\"pdf\"/\"rotation_degrees\"")]
        [InlineData("tgs", "\"lottie\"/\"frame_rate\"")]
        [InlineData("docx", "\"ooxml\"/\"main_part\"")]
        public async Task DocumentsAndAnimationExposeNativePropertiesThroughTheCommonProbe(string format, string expected)
        {
            byte[] encoded;
            switch (format)
            {
                case "svg":
                    encoded = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 320 200\"/>"u8.ToArray();
                    break;
                case "pdf":
                    encoded = PdfTestDocument.Create("", "/Rotate 90");
                    break;
                case "tgs":
                    encoded = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Tgs", "orbit.tgs"));
                    break;
                case "docx":
                    encoded = WordTestInput.Create("<w:p/>");
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(format));
            }
            string path = Path.Combine(directory, "document.bin");
            await File.WriteAllBytesAsync(path, encoded);
            (int code, string output, string error) = await CliTests.RunAsync(["probe", path]);
            Assert.True(code == 0, error);
            Assert.Contains(expected, output, StringComparison.Ordinal);
            Assert.Contains("Metadata ", output, StringComparison.Ordinal);
            Assert.Single(Directory.GetFiles(directory));
        }

        public void Dispose() => Directory.Delete(directory, true);
    }
}
