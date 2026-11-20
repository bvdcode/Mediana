// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Cli;
using Mediana.Core.Video;
using Mediana.IO;

namespace Mediana.Tests
{
    [Collection("CommandLine")]
    public class CliPresentationTests : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("mediana-presentation-").FullName;

        [Theory]
        [InlineData("preview")]
        [InlineData("video-preview")]
        [InlineData("animation")]
        [InlineData("transcode")]
        [InlineData("hls")]
        public async Task VideoOperationsUseCompatibleLibraryDecoding(string operation)
        {
            string input = Path.Combine(AppContext.BaseDirectory, "Data", "Metadata", "avc-interlaced.mp4");
            string output = Path.Combine(directory, operation switch
            {
                "preview" or "video-preview" => "preview.png",
                "animation" => "animation.webp",
                "transcode" => "video.ts",
                "hls" => "hls",
                _ => throw new ArgumentOutOfRangeException(nameof(operation))
            });
            string[] arguments = operation switch
            {
                "preview" => ["-i", input, output],
                "video-preview" => ["video", "preview", input, output],
                "animation" => ["-i", input, output, "--animate"],
                "transcode" => ["-i", input, output, "--video-codec", "H264", "--video-only"],
                "hls" => ["hls", "encode", input, output, "--video-only", "--widths", "64"],
                _ => throw new ArgumentOutOfRangeException(nameof(operation))
            };
            (int code, _, string error) = await CliTests.RunAsync(arguments);
            Assert.True(code == 0, error);
            if (operation == "hls")
            {
                Assert.NotEmpty(Directory.EnumerateFiles(output, "*.m3u8", SearchOption.AllDirectories));
            }
            else
            {
                Assert.True(new FileInfo(output).Length > 0);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ConfiguredFactoriesDeliverRecoveryDiagnostics(bool visual)
        {
            ProgressTestContext logger = new();
            MediaEngine engine = visual ? ImageEngineFactory.CreateVisual(logger) : ContainerEngineFactory.CreateVideoBuilder(logger).Build();
            string input = Path.Combine(AppContext.BaseDirectory, "Data", "Metadata", "avc-interlaced.mp4");
            await using RandomAccessStreamByteSource source = new(File.OpenRead(input));
            await using VideoSession session = await engine.OpenVideoAsync(source, 0);
            using VideoFrame frame = Assert.IsType<VideoFrame>(await session.ReadFrameAsync());
            Assert.Single(logger.Messages, message => message.Contains("inconsistent H.264 picture-timing", StringComparison.Ordinal));
        }

        public void Dispose()
        {
            Directory.Delete(directory, recursive: true);
            GC.SuppressFinalize(this);
        }
    }
}
