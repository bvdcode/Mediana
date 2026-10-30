// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Globalization;
using System.Buffers.Binary;
using Mediana.Cli;

namespace Mediana.Tests
{
    [Collection("CommandLine")]
    public class CliTests : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("mediana-cli-").FullName;

        [Theory]
        [InlineData("")]
        [InlineData("audio decode")]
        [InlineData("audio waveform")]
        [InlineData("video preview")]
        [InlineData("image heic")]
        [InlineData("image svg")]
        [InlineData("image tgs")]
        [InlineData("pdf render")]
        [InlineData("hls create")]
        public async Task HelpDoesNotRequireInputOrCreateOutput(string command)
        {
            string[] arguments = [.. command.Split(' ', StringSplitOptions.RemoveEmptyEntries), "--help"];
            (int code, string output, string error) = await RunAsync(arguments);
            Assert.Equal(0, code);
            Assert.Contains("Usage:", output, StringComparison.Ordinal);
            Assert.StartsWith("Mediana ", error, StringComparison.Ordinal);
            Assert.Contains("Vadim Belov (bvdcode)", error, StringComparison.Ordinal);
            Assert.Single(error.Split('\n', StringSplitOptions.RemoveEmptyEntries));
            Assert.Empty(Directory.GetFileSystemEntries(directory));
        }

        [Theory]
        [InlineData("unknown")]
        [InlineData("audio decode")]
        [InlineData("audio decode input output --stream bad")]
        [InlineData("pdf render input output --unknown")]
        [InlineData("probe mp4 input --log-level bad")]
        [InlineData("probe mp4 input --log-level 999")]
        public async Task InvalidSyntaxHasUsageExitCode(string command)
        {
            (int code, _, _) = await RunAsync(command.Split(' '));
            Assert.Equal(2, code);
        }

        [Fact]
        public async Task ExistingDestinationSurvivesCommandFailure()
        {
            string input = Data("Pdf", "report.pdf");
            string output = Path.Combine(directory, "existing.png");
            await File.WriteAllBytesAsync(output, [1, 2, 3]);
            (int code, _, _) = await RunAsync(["pdf", "render", input, output]);
            Assert.Equal(5, code);
            Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(output));
            Assert.Single(Directory.GetFiles(directory));
        }

        [Fact]
        public async Task UnsupportedAnimationPublishesNothing()
        {
            string output = Path.Combine(directory, "unsupported.png");
            (int code, _, _) = await RunAsync(["image", "tgs", Data("Tgs", "controller-expressions.tgs"), output]);
            Assert.Equal(3, code);
            Assert.Empty(Directory.GetFiles(directory));
        }

        [Fact]
        public async Task InvalidPdfHasInvalidInputExitCode()
        {
            string input = Path.Combine(directory, "invalid.pdf");
            await File.WriteAllTextAsync(input, "This is not a PDF.");
            string output = Path.Combine(directory, "page.png");
            (int code, _, _) = await RunAsync(["pdf", "render", input, output]);
            Assert.Equal(4, code);
            Assert.False(File.Exists(output));
            Assert.Single(Directory.GetFiles(directory));
        }

        [Theory]
        [InlineData("--page", "0")]
        [InlineData("--width", "-1")]
        public async Task InvalidPageOptionsPublishNothing(string option, string value)
        {
            string output = Path.Combine(directory, "invalid.png");
            (int code, _, _) = await RunAsync(["pdf", "render", Data("Pdf", "report.pdf"), output, option, value]);
            Assert.Equal(2, code);
            Assert.Empty(Directory.GetFiles(directory));
        }

        [Fact]
        public async Task ExternalCancellationReturns130WithoutPublishing()
        {
            using CancellationTokenSource source = new();
            await source.CancelAsync();
            string output = Path.Combine(directory, "cancelled.png");
            (int code, _, _) = await RunAsync(["pdf", "render", Data("Pdf", "report.pdf"), output], source.Token);
            Assert.Equal(130, code);
            Assert.Empty(Directory.GetFiles(directory));
        }

        [Fact]
        public async Task CancellationAfterWritingRemovesTemporaryOutput()
        {
            using CancellationTokenSource source = new();
            string output = Path.Combine(directory, "cancelled.bin");
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CommandOutputFile.WriteAsync(output, async stream =>
            {
                await stream.WriteAsync(new byte[] { 1, 2, 3 });
                await source.CancelAsync();
            }, source.Token));
            Assert.Empty(Directory.GetFiles(directory));
        }

        [Fact]
        public async Task HelpAndInvocationRestoreTheCallingExitCode()
        {
            int previous = Environment.ExitCode;
            Environment.ExitCode = 17;
            try
            {
                (int code, _, _) = await RunAsync(["--help"]);
                Assert.Equal(0, code);
                Assert.Equal(17, Environment.ExitCode);
            }
            finally
            {
                Environment.ExitCode = previous;
            }
        }

        [Theory]
        [InlineData(".wav")]
        [InlineData(".wave")]
        [InlineData(".WAVE")]
        public async Task SimpleConversionDetectsInputByContentAndWritesRealWave(string extension)
        {
            string input = Path.Combine(directory, "input.bin");
            await File.WriteAllBytesAsync(input, await File.ReadAllBytesAsync(Data("Wave", "s16-stereo.wav")));
            string output = Path.Combine(directory, "output" + extension);
            (int code, _, string error) = await RunAsync(["-i", input, output, "--no-color"]);
            Assert.Equal(0, code);
            Assert.StartsWith("Mediana ", error, StringComparison.Ordinal);
            byte[] bytes = await File.ReadAllBytesAsync(output);
            Assert.Equal("RIFF"u8.ToArray(), bytes.AsSpan(0, 4).ToArray());
            Assert.Equal("WAVE"u8.ToArray(), bytes.AsSpan(8, 4).ToArray());
            Assert.Equal(bytes.Length - 8, (long)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4)));
            Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(20)));
            Assert.Equal(16, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(34)));
        }

        [Fact]
        public async Task StartupAndVersionOptionReportTheBuildVersion()
        {
            (int code, string output, string error) = await RunAsync(["--version"]);
            Assert.Equal(0, code);
            Assert.Matches(@"^\d+\.\d+\.\d+", output);
            Assert.StartsWith("Mediana " + output.Trim() + " | Copyright (c) 2026 Vadim Belov (bvdcode)", error, StringComparison.Ordinal);
        }

        [Fact]
        public async Task SimpleWaveConversionSelectsAudioWhenVideoIsTheFirstTrack()
        {
            string output = Path.Combine(directory, "audio.wav");
            (int code, _, _) = await RunAsync(["-i", Data("Mp4", "avc-aac.mp4"), output]);
            Assert.Equal(0, code);
            byte[] bytes = await File.ReadAllBytesAsync(output);
            Assert.Equal("WAVE"u8.ToArray(), bytes.AsSpan(8, 4).ToArray());
            Assert.True(bytes.Length > 44);
        }

        [Theory]
        [InlineData("s16-stereo")]
        [InlineData("s16le-stereo")]
        public async Task AiffSignatureSelectsTheContainerAndConvertsAllSamples(string name)
        {
            string input = Path.Combine(directory, "input.bin");
            await File.WriteAllBytesAsync(input, await File.ReadAllBytesAsync(Data("Aiff", name + ".aiff")));
            string output = Path.Combine(directory, "output.wav");
            (int code, _, _) = await RunAsync(["-i", input, output, "--no-color"]);
            Assert.Equal(0, code);
            byte[] wave = await File.ReadAllBytesAsync(output);
            byte[] expected = await File.ReadAllBytesAsync(Data("Wave", "s16-stereo.pcm"));
            Assert.Equal(expected, wave.AsSpan(44).ToArray());
        }

        [Theory]
        [InlineData("Pdf", "report.pdf")]
        [InlineData("Heif", "basic-8bit.heic")]
        [InlineData("Jpeg2000", "L-63x45.jp2")]
        [InlineData("Jbig2", "generic-t0-tp0-at1.jb2")]
        [InlineData("Svg", "vector-preview.svg")]
        [InlineData("Tgs", "geometry.tgs")]
        [InlineData("Mp4", "avc-weighted.mp4")]
        public async Task SimplePngConversionDetectsInputWithoutUsingItsExtension(string format, string name)
        {
            string input = Path.Combine(directory, "source.bin");
            await File.WriteAllBytesAsync(input, await File.ReadAllBytesAsync(Data(format, name)));
            string output = Path.Combine(directory, "output.png");
            (int code, _, _) = await RunAsync(["-i", input, output]);
            Assert.Equal(0, code);
            byte[] bytes = await File.ReadAllBytesAsync(output);
            Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, bytes.AsSpan(0, 8).ToArray());
        }

        private static string Data(string format, string name) => Path.Combine(AppContext.BaseDirectory, "Data", format, name);

        [Fact]
        public async Task ExplicitVideoEncodingWritesTransportPackets()
        {
            string output = Path.Combine(directory, "encoded.ts");
            (int code, _, _) = await RunAsync(["-i", Data("Mp4", "avc-multislice-cabac.mp4"), output,
                "--video-codec", "H264", "--video-only", "--qp", "26"]);
            Assert.Equal(0, code);
            byte[] encoded = await File.ReadAllBytesAsync(output);
            Assert.Equal(0, encoded.Length % 188);
            for (int offset = 0; offset < encoded.Length; offset += 188)
            {
                Assert.Equal(0x47, encoded[offset]);
            }
        }

        [Theory]
        [InlineData("avc-aac.mp4", false, 26, 3)]
        [InlineData("avc-weighted-rotated.mp4", true, 26, 3)]
        [InlineData("avc-aac.mp4", true, 52, 2)]
        public async Task UnsupportedEncodingOptionsOrDisplayTransformsPublishNothing(string input, bool videoOnly, int qp, int expected)
        {
            string output = Path.Combine(directory, "encoded.ts");
            List<string> arguments = ["-i", Data("Mp4", input), output, "--video-codec", "H264", "--qp", qp.ToString(CultureInfo.InvariantCulture)];
            if (videoOnly)
            {
                arguments.Add("--video-only");
            }
            (int code, _, _) = await RunAsync(arguments.ToArray());
            Assert.Equal(expected, code);
            Assert.Empty(Directory.GetFiles(directory));
        }

        private static async Task<(int Code, string Output, string Error)> RunAsync(string[] arguments, CancellationToken token = default)
        {
            using StringWriter output = new(CultureInfo.InvariantCulture);
            using StringWriter error = new(CultureInfo.InvariantCulture);
            int code = await CliApplication.RunAsync(arguments, output, error, token);
            return (code, output.ToString(), error.ToString());
        }

        public void Dispose()
        {
            Directory.Delete(directory, recursive: true);
            GC.SuppressFinalize(this);
        }
    }
}
