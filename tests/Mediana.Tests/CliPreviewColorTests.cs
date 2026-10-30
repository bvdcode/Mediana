// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Cli;
using Mediana.Codecs.H264;
using Mediana.Codecs.Png;
using Mediana.Containers.Mp4;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.IO;
using Mediana.Processing;

namespace Mediana.Tests
{
    [Collection("CommandLine")]
    public class CliPreviewColorTests : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("mediana-preview-color-").FullName;
        private static string Input => Path.Combine(AppContext.BaseDirectory, "Data", "Preview", "untagged-avc.mp4");

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task BothPreviewCommandsProducePngForVideoWithoutColorTags(bool explicitCommand)
        {
            string output = Path.Combine(directory, "preview.png");
            string[] args = explicitCommand ? ["video", "preview", Input, output] : ["-i", Input, output];
            (int code, _, string error) = await CliTests.RunAsync(args);
            Assert.True(code == 0, error);
            Dictionary<string, byte[]> chunks = PngTestInput.ReadChunks(await File.ReadAllBytesAsync(output));
            Assert.NotEmpty(chunks["IDAT"]);
            Assert.Single(Directory.GetFiles(directory));
        }

        [Fact]
        public async Task InferenceLogsOnceProducesExplicitInterpretationAndLeavesLibraryMetadataStrict()
        {
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new Mp4ReaderFactory()).AddVideoDecoder(new H264DecoderFactory()).Build();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Input));
            await using VideoSession session = await engine.OpenVideoAsync(source);
            using VideoFrame frame = await session.ReadFrameAsync() ?? throw new InvalidDataException("Missing fixture frame.");
            Assert.Equal(ColorPrimaries.Unspecified, frame.Format.Color.Primaries);
            Assert.Equal(TransferFunction.Unspecified, frame.Format.Color.Transfer);
            Assert.Equal(MatrixCoefficients.Unspecified, frame.Format.Color.Matrix);
            MediaBufferAllocator allocator = new(1024 * 1024);
            Assert.Throws<NotSupportedException>(() => SrgbPreview.Fit(frame, VideoDisplayMatrix.Identity, 96, 64, allocator));
            VideoColorInfo explicitColor = new(ColorPrimaries.Smpte170M, TransferFunction.Bt709, MatrixCoefficients.Bt601,
                ColorRange.Limited, ChromaLocation.Left);
            byte[] expected;
            using (VideoFrame preview = SrgbPreview.Fit(frame, explicitColor, VideoDisplayMatrix.Identity, 96, 64, allocator))
            using (MediaBuffer png = PngEncoder.Encode(preview, allocator))
            {
                expected = png.Memory.ToArray();
            }
            Assert.Equal(0, allocator.AllocatedBytes);
            string output = Path.Combine(directory, "preview.png");
            ProgressTestContext logger = new();
            await VideoPreviewCommand.RunAsync(Input, new CommandOutputFile(output, false), null, 0, 96, 64, logger);
            Assert.Equal(expected, await File.ReadAllBytesAsync(output));
            string warning = Assert.Single(logger.Messages);
            Assert.Contains("primaries, transfer, matrix", warning, StringComparison.Ordinal);
            Assert.Contains("primaries Smpte170M, transfer Bt709, matrix Bt601", warning, StringComparison.Ordinal);
            Assert.Equal(ColorPrimaries.Unspecified, frame.Format.Color.Primaries);
        }

        [Fact]
        public async Task CompleteColorMetadataProducesNoInferenceWarning()
        {
            string input = Path.Combine(AppContext.BaseDirectory, "Data", "Mp4", "avc-weighted.mp4");
            string output = Path.Combine(directory, "preview.png");
            ProgressTestContext logger = new();
            await VideoPreviewCommand.RunAsync(input, new CommandOutputFile(output, false), null, 0, 32, 32, logger);
            Assert.Empty(logger.Messages);
            Assert.True(File.Exists(output));
        }

        public void Dispose()
        {
            Directory.Delete(directory, recursive: true);
            GC.SuppressFinalize(this);
        }
    }
}
