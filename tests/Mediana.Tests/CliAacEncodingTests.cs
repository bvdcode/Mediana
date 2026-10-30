// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    [Collection("CommandLine")]
    public class CliAacEncodingTests : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("mediana-aac-").FullName;

        [Theory]
        [InlineData("lc-mono-48", 64000)]
        [InlineData("lc-stereo-44", 128000)]
        public async Task RootCommandEncodesCompleteAacTransport(string name, int bitRate)
        {
            string source = Path.Combine(AppContext.BaseDirectory, "Data", "Aac", name + ".wav");
            string output = Path.Combine(directory, "encoded.ts");
            (int code, _, _) = await CliTests.RunAsync(["-i", source, output, "--audio-codec", "Aac", "--audio-bit-rate", bitRate.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
            Assert.Equal(0, code);
            byte[] transport = await File.ReadAllBytesAsync(output);
            Assert.True(transport.Length > 188);
            Assert.Equal(0, transport.Length % 188);
            for (int index = 0; index < transport.Length; index += 188)
            {
                Assert.Equal(0x47, transport[index]);
            }
            Assert.Single(Directory.GetFiles(directory));
        }

        [Theory]
        [InlineData("bad.wav", "--audio-codec Aac", 3)]
        [InlineData("bad.ts", "--audio-bit-rate 64000", 3)]
        [InlineData("bad.ts", "--audio-codec Aac --video-only", 3)]
        [InlineData("bad.ts", "--audio-codec Aac --audio-bit-rate 1", 2)]
        public async Task InvalidEncodingOptionsPublishNoFile(string name, string options, int expected)
        {
            string source = Path.Combine(AppContext.BaseDirectory, "Data", "Aac", "lc-mono-48.wav");
            List<string> arguments = ["-i", source, Path.Combine(directory, name)];
            arguments.AddRange(options.Split(' '));
            (int code, _, _) = await CliTests.RunAsync(arguments.ToArray());
            Assert.Equal(expected, code);
            Assert.Empty(Directory.GetFileSystemEntries(directory));
        }

        public void Dispose()
        {
            Directory.Delete(directory, recursive: true);
            GC.SuppressFinalize(this);
        }
    }
}
