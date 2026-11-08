// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;

namespace Mediana.Tests
{
    [Collection("CommandLine")]
    public class CliAv1Tests : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("mediana-av1-").FullName;

        [Theory]
        [InlineData("interOrdered", "mp4", 48, 40)]
        [InlineData("interOrdered", "mkv", 48, 40)]
        [InlineData("main10Default", "mp4", 160, 136)]
        [InlineData("main10Default", "mkv", 160, 136)]
        public async Task GenericCommandsProbePreviewTranscodeAndWriteHls(string fixture, string extension, int width, int height)
        {
            string input = Path.Combine(AppContext.BaseDirectory, "Data", "Av1", fixture + "." + extension);
            (int probeCode, string probe, string probeError) = await CliTests.RunAsync(["-i", input]);
            Assert.True(probeCode == 0, probeError);
            Assert.Contains("; av1;", probe, StringComparison.Ordinal);
            string png = Path.Combine(directory, "preview.png");
            (int previewCode, _, string previewError) = await CliTests.RunAsync(["-i", input, png, "--time-ms", "84"]);
            Assert.True(previewCode == 0, previewError);
            byte[] image = await File.ReadAllBytesAsync(png);
            Assert.True(image.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }));
            Assert.Equal((uint)width, BinaryPrimitives.ReadUInt32BigEndian(image.AsSpan(16, 4)));
            Assert.Equal((uint)height, BinaryPrimitives.ReadUInt32BigEndian(image.AsSpan(20, 4)));
            string ts = Path.Combine(directory, "encoded.ts");
            (int encodeCode, _, string encodeError) = await CliTests.RunAsync(["-i", input, ts, "--video-codec", "H264"]);
            Assert.True(encodeCode == 0, encodeError);
            Assert.True(new FileInfo(ts).Length > 0);
            Assert.Equal(0, new FileInfo(ts).Length % 188);
            string hls = Path.Combine(directory, "hls");
            (int hlsCode, _, string hlsError) = await CliTests.RunAsync(["hls", "encode", input, hls, "--video-only", "--segment-ms", "100", "--widths", "48"]);
            Assert.True(hlsCode == 0, hlsError);
            Assert.True(File.Exists(Path.Combine(hls, "index.m3u8")));
            Assert.NotEmpty(Directory.GetFiles(hls, "*.ts", SearchOption.AllDirectories));
        }

        public void Dispose()
        {
            Directory.Delete(directory, recursive: true);
            GC.SuppressFinalize(this);
        }
    }
}
