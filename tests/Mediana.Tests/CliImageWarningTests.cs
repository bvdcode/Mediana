// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Diagnostics;

namespace Mediana.Tests
{
    [Collection("CommandLine")]
    public class CliImageWarningTests : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("mediana-image-warnings-").FullName;

        [Theory]
        [InlineData("convert")]
        [InlineData("render")]
        [InlineData("image")]
        public async Task PdfRecoveryReportsOneWarningAndCreatesPng(string operation)
        {
            string input = Path.Combine(directory, "input.bin");
            string output = Path.Combine(directory, "output.png");
            await File.WriteAllBytesAsync(input, PdfRestartTestInput.Create());
            string[] arguments = operation switch
            {
                "convert" => ["-i", input, output],
                "render" => ["pdf", "render", input, output],
                "image" => ["pdf", "image", input, output],
                _ => throw new ArgumentOutOfRangeException(nameof(operation))
            };
            (int code, string text, string error) = await RunAsync(arguments);
            Assert.True(code == 0, text + error);
            Assert.Equal(1, (text + error).Split(JpegRestartTestInput.Warning, StringSplitOptions.None).Length - 1);
            byte[] png = await File.ReadAllBytesAsync(output);
            Assert.True(png.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }));
        }

        [Fact]
        public async Task StandaloneJpegReportsRecoveryThroughTheCommonImageCommand()
        {
            string input = Path.Combine(directory, "input.bin");
            string output = Path.Combine(directory, "output.png");
            await File.WriteAllBytesAsync(input, JpegTestData.Insert(JpegRestartTestInput.Create(), 224, [.. "JFIF\0"u8, 1, 2, 0, 0, 1, 0, 1, 0, 0]));
            (int code, string text, string error) = await RunAsync(["-i", input, output]);
            Assert.True(code == 0, text + error);
            Assert.Equal(1, (text + error).Split(JpegRestartTestInput.Warning, StringSplitOptions.None).Length - 1);
            Assert.True(File.Exists(output));
        }

        [Fact]
        public async Task InvalidFinalRestartKeepsAnExistingDestination()
        {
            string input = Path.Combine(directory, "input.bin");
            string output = Path.Combine(directory, "output.png");
            byte[] encoded = JpegRestartTestInput.Create();
            encoded[^3] = 209;
            await File.WriteAllBytesAsync(input, encoded);
            byte[] original = [1, 2, 3];
            await File.WriteAllBytesAsync(output, original);
            (int code, string text, string error) = await RunAsync(["-i", input, output, "--overwrite"]);
            Assert.Equal(4, code);
            Assert.DoesNotContain(JpegRestartTestInput.Warning, text + error, StringComparison.Ordinal);
            Assert.Equal(original, await File.ReadAllBytesAsync(output));
        }

        [Theory]
        [InlineData("convert")]
        [InlineData("tgs")]
        public async Task LottieStoredValuesProducePngAndOneWarningPerOmission(string operation)
        {
            string input = Path.Combine(directory, "input.bin");
            string output = Path.Combine(directory, "output.png");
            await File.WriteAllBytesAsync(input, TgsStoredValuesTestInput.Encoded);
            string[] arguments = operation switch
            {
                "convert" => ["-i", input, output],
                "tgs" => ["image", "tgs", input, output],
                _ => throw new ArgumentOutOfRangeException(nameof(operation))
            };
            (int code, string text, string error) = await RunAsync(arguments);
            Assert.True(code == 0, text + error);
            foreach (string warning in new[] { TgsStoredValuesTestInput.PuppetWarning, TgsStoredValuesTestInput.ExpressionWarning })
            {
                Assert.Equal(1, (text + error).Split(warning, StringSplitOptions.None).Length - 1);
            }
            byte[] png = await File.ReadAllBytesAsync(output);
            Assert.True(png.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }));
        }

        private static async Task<(int Code, string Output, string Error)> RunAsync(string[] arguments)
        {
            string executable = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "Mediana.Cli.exe" : "Mediana.Cli");
            ProcessStartInfo start = new(executable) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (string argument in arguments) { start.ArgumentList.Add(argument); }
            start.ArgumentList.Add("--no-color");
            using Process process = Process.Start(start) ?? throw new InvalidOperationException("The command did not start.");
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
            try
            {
                Task<string> output = process.StandardOutput.ReadToEndAsync(timeout.Token);
                Task<string> error = process.StandardError.ReadToEndAsync(timeout.Token);
                await process.WaitForExitAsync(timeout.Token);
                return (process.ExitCode, await output, await error);
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }
            }
        }

        public void Dispose() => Directory.Delete(directory, recursive: true);
    }
}
