// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    [Collection("CommandLine")]
    public class CliHlsContainerTests
    {
        [Fact]
        public async Task AviWithPcmProducesAudiovisualHlsVariants()
        {
            string directory = Directory.CreateTempSubdirectory("mediana-hls-").FullName;
            try
            {
                string input = Path.Combine(AppContext.BaseDirectory, "Data", "Avi", "mjpeg-pcm.avi");
                string output = Path.Combine(directory, "output");
                (int code, _, string error) = await CliTests.RunAsync(["hls", "encode", input, output,
                    "--audio-stream", "1", "--audio-codec", "Aac", "--segment-ms", "500", "--widths", "64", "32"]);
                Assert.True(code == 0, error);
                Assert.Equal(3, Directory.GetFiles(output, "*.m3u8", SearchOption.AllDirectories).Length);
                Assert.Equal(6, Directory.GetFiles(output, "*.ts", SearchOption.AllDirectories).Length);
            }
            finally { Directory.Delete(directory, recursive: true); }
        }
    }
}
