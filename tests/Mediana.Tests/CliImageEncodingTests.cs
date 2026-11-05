// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;

namespace Mediana.Tests
{
    [Collection("CommandLine")]
    public class CliImageEncodingTests : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("mediana-image-encoding-").FullName;

        [Fact]
        public async Task LossyWebpUsesExplicitQualityAndCommonInputRecognition()
        {
            string input = Path.Combine(AppContext.BaseDirectory, "Data", "Tgs", "geometry.json");
            string output = Path.Combine(directory, "image.webp");
            (int code, _, string error) = await CliTests.RunAsync(["-i", input, output, "--image-compression", "Lossy", "--quality", "80"]);
            Assert.True(code == 0, error);
            byte[] file = await File.ReadAllBytesAsync(output);
            Assert.True(file.AsSpan(8, 4).SequenceEqual("WEBP"u8));
            Assert.Contains("VP8 ", System.Text.Encoding.ASCII.GetString(file), StringComparison.Ordinal);
        }

        [Fact]
        public async Task TgsAnimationExportsAllFramesWithRoundedBoundaryTiming()
        {
            string input = Path.Combine(directory, "animation.json");
            string output = Path.Combine(directory, "animation.webp");
            await File.WriteAllTextAsync(input, "{\"w\":2,\"h\":1,\"fr\":24,\"ip\":10,\"op\":34,\"layers\":[]}");
            (int code, string text, string error) = await CliTests.RunAsync(["-i", input, output, "--animate"]);
            Assert.True(code == 0, error);
            Assert.Contains("24 frames", text, StringComparison.Ordinal);
            byte[] file = await File.ReadAllBytesAsync(output);
            int frames = 0;
            int duration = 0;
            for (int position = 12; position < file.Length;)
            {
                int length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(position + 4)));
                if (file.AsSpan(position, 4).SequenceEqual("ANMF"u8))
                {
                    frames++;
                    duration += file[position + 20] | file[position + 21] << 8 | file[position + 22] << 16;
                }
                position += length + 8 + (length & 1);
            }
            Assert.Equal(24, frames);
            Assert.Equal(1000, duration);
        }

        [Theory]
        [InlineData("output.png", "--quality", "75")]
        [InlineData("output.webp", "--quality", "75")]
        [InlineData("output.webp", "--image-compression", "999")]
        [InlineData("output.png", "--animate", "true")]
        public async Task IncompatibleOutputSettingsPublishNothing(string name, string option, string value)
        {
            string input = Path.Combine(AppContext.BaseDirectory, "Data", "Tgs", "geometry.json");
            string output = Path.Combine(directory, name);
            (int code, _, _) = await CliTests.RunAsync(["-i", input, output, option, value]);
            Assert.NotEqual(0, code);
            Assert.False(File.Exists(output));
            Assert.Empty(Directory.GetFiles(directory));
        }

        public void Dispose() => Directory.Delete(directory, recursive: true);
    }
}
