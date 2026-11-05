// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Cli;
using Mediana.Codecs.H264;
using Mediana.Containers.Mp4;
using Mediana.Core.Video;
using Mediana.IO;

namespace Mediana.Tests
{
    [Collection("CommandLine")]
    public class CliContainerColorTests : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("mediana-container-color-").FullName;

        [Theory]
        [InlineData("container-709-limited.mp4")]
        [InlineData("container-709-full.mp4")]
        [InlineData("container-709.mov")]
        [InlineData("container-601-conflict.mp4")]
        public async Task ContainerLabelsProducePngWithoutAnInferenceWarning(string file)
        {
            string output = Path.Combine(directory, "preview.png");
            ProgressTestContext logger = new();
            await VideoPreviewCommand.RunAsync(Mp4ColorTestData.PathFor(file), new CommandOutputFile(output, false), null, 0, 96, 64, logger, new Mediana.Codecs.Png.PngImageEncoder());
            Assert.Empty(logger.Messages);
            Assert.NotEmpty(PngTestInput.ReadChunks(await File.ReadAllBytesAsync(output))["IDAT"]);
            Assert.Single(Directory.GetFiles(directory));
        }

        [Fact]
        public async Task ProbeShowsContainerDeclarationsAndOriginalCodecColorSeparately()
        {
            (int code, string output, string error) = await CliTests.RunAsync(["probe", Mp4ColorTestData.PathFor("container-601-conflict.mp4")]);
            Assert.True(code == 0, error);
            Assert.Contains("Source color: CICP primaries 6; transfer 1; matrix 6; range full.", output, StringComparison.Ordinal);
            Assert.Contains("Color: primaries Bt709; transfer Bt709; matrix Bt709; range Limited", output, StringComparison.Ordinal);
        }

        [Fact]
        public async Task ContainerHdrDeclarationCannotBeReplacedBySdrInference()
        {
            string output = Path.Combine(directory, "preview.png");
            (int code, _, _) = await CliTests.RunAsync(["-i", Mp4ColorTestData.PathFor("container-2020-pq.mp4"), output]);
            Assert.Equal(CliExitCodes.Unsupported, code);
            NotSupportedException error = await Assert.ThrowsAsync<NotSupportedException>(() => VideoPreviewCommand.RunAsync(
                Mp4ColorTestData.PathFor("container-2020-pq.mp4"), new CommandOutputFile(output, false), null, 0, 96, 64, new ProgressTestContext(), new Mediana.Codecs.Png.PngImageEncoder()));
            Assert.Contains("tone mapping", error.Message, StringComparison.Ordinal);
            Assert.Empty(Directory.GetFiles(directory));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task IccAndReservedCodesAreInspectableButNeverGuessedAsSdr(bool icc)
        {
            byte[] payload = icc ? [.. "prof"u8, 1, 2, 3] : Mp4ColorTestData.Cicp(primaries: 65535);
            string input = Path.Combine(directory, "input.bin");
            string output = Path.Combine(directory, "preview.png");
            await File.WriteAllBytesAsync(input, await Mp4ColorTestData.ReplaceAsync(payload));
            (int probeCode, string probe, string probeError) = await CliTests.RunAsync(["probe", input]);
            Assert.True(probeCode == 0, probeError);
            Assert.Contains(icc ? "unrestricted ICC profile; 3 bytes" : "CICP primaries 65535", probe, StringComparison.Ordinal);
            (int code, _, _) = await CliTests.RunAsync(["-i", input, output]);
            Assert.Equal(CliExitCodes.Unsupported, code);
            Assert.Single(Directory.GetFiles(directory));
            Assert.False(File.Exists(output));
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new Mp4ReaderFactory()).AddVideoDecoder(new H264DecoderFactory()).Build();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(input));
            await using VideoSession session = await engine.OpenVideoAsync(source);
            using VideoFrame frame = await session.ReadFrameAsync() ?? throw new InvalidDataException("No raw decoded frame.");
            Assert.NotEmpty(frame.Data.ToArray());
            NotSupportedException error = Assert.Throws<NotSupportedException>(() => frame.Format.GetColorInterpretation());
            Assert.Contains(icc ? "ICC profile" : "unsupported codes", error.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task UnspecifiedContainerTransferIsInferredWithoutReplacingItsOtherLabels()
        {
            string input = Path.Combine(directory, "input.bin");
            string output = Path.Combine(directory, "preview.png");
            await File.WriteAllBytesAsync(input, await Mp4ColorTestData.ReplaceAsync(Mp4ColorTestData.Cicp(transfer: 2)));
            ProgressTestContext logger = new();
            await VideoPreviewCommand.RunAsync(input, new CommandOutputFile(output, false), null, 0, 96, 64, logger, new Mediana.Codecs.Png.PngImageEncoder());
            string warning = Assert.Single(logger.Messages);
            Assert.Contains("metadata (transfer)", warning, StringComparison.Ordinal);
            Assert.Contains("primaries Bt709, transfer Bt709, matrix Bt709", warning, StringComparison.Ordinal);
            Assert.True(File.Exists(output));
        }

        public void Dispose()
        {
            Directory.Delete(directory, recursive: true);
            GC.SuppressFinalize(this);
        }
    }
}
