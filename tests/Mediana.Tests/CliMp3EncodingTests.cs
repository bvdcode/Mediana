// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.Mp3;
using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana.Tests
{
    [Collection("CommandLine")]
    public class CliMp3EncodingTests
    {
        [Fact]
        public async Task Mp4AudioProducesInspectableMp3WithExplicitPresentationSelection()
        {
            string directory = Directory.CreateTempSubdirectory("mediana-mp3-").FullName;
            try
            {
                string input = Path.Combine(AppContext.BaseDirectory, "Data", "Mp4", "avc-aac.mp4");
                string output = Path.Combine(directory, "output.mp3");
                (int code, string text, string error) = await CliTests.RunAsync(["-i", input, output]);
                Assert.True(code == 0, error);
                Assert.Contains("mp3 / mp3:", text, StringComparison.Ordinal);
                Assert.True(new FileInfo(output).Length > 256);
                MediaBufferAllocator allocator = new(1024 * 1024);
                await using RandomAccessStreamByteSource source = new(File.OpenRead(output));
                await using (Mp3Reader reader = await Mp3Reader.OpenAsync(source, allocator))
                {
                    Assert.NotNull(reader.GetPresentationRange(0));
                    Assert.NotNull(reader.VbrInfo!.FrameCount);
                }
                Assert.Equal(0, allocator.AllocatedBytes);
            }
            finally { Directory.Delete(directory, recursive: true); }
        }
    }
}
