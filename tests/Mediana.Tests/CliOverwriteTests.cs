// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Globalization;
using Mediana.Cli;

namespace Mediana.Tests
{
    [Collection("CommandLine")]
    public class CliOverwriteTests : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("mediana-overwrite-").FullName;

        [Theory]
        [InlineData("-y", false)]
        [InlineData("--yes", false)]
        [InlineData("--overwrite", false)]
        [InlineData("-y", true)]
        [InlineData("--yes", true)]
        [InlineData("--overwrite", true)]
        public async Task RootAndNamedCommandsReplaceOnlyAfterSuccessfulConversion(string option, bool named)
        {
            string input = Path.Combine(AppContext.BaseDirectory, "Data", "Pdf", "report.pdf");
            string output = Path.Combine(directory, "page.png");
            await File.WriteAllBytesAsync(output, [1, 2, 3]);
            string[] arguments = named ? ["pdf", "render", input, output, option] : ["-i", input, output, option];
            (int code, _, _) = await CliTests.RunAsync(arguments);
            Assert.Equal(0, code);
            byte[] bytes = await File.ReadAllBytesAsync(output);
            Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, bytes[..8]);
            Assert.Single(Directory.GetFiles(directory));
        }

        [Theory]
        [InlineData("y")]
        [InlineData("Y")]
        [InlineData("yes")]
        [InlineData(" YES ")]
        public async Task InteractiveConfirmationAuthorizesAtomicReplacement(string answer)
        {
            string output = await ExistingFileAsync();
            using StringReader input = new(answer + "\n");
            using StringWriter diagnostics = new(CultureInfo.InvariantCulture);
            CommandConfirmation confirmation = new(false, true, input, diagnostics);
            CommandOutputPolicy policy = new(false, confirmation);
            CommandOutputFile file = Assert.IsType<CommandOutputFile>(await policy.PrepareFileAsync("input.mp3", output));
            Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(output));
            Assert.Contains("Overwrite? [y/N]", diagnostics.ToString(), StringComparison.Ordinal);
            await file.WriteAsync(stream => stream.WriteAsync(new byte[] { 7, 8 }).AsTask());
            Assert.Equal(new byte[] { 7, 8 }, await File.ReadAllBytesAsync(output));
            Assert.Single(Directory.GetFiles(directory));
        }

        [Theory]
        [InlineData("n\n")]
        [InlineData("\n")]
        [InlineData("")]
        public async Task DecliningBlankAnswerAndEofLeaveTheFileUntouched(string answer)
        {
            string output = await ExistingFileAsync();
            using StringReader input = new(answer);
            using StringWriter diagnostics = new(CultureInfo.InvariantCulture);
            CommandOutputPolicy policy = new(false, new CommandConfirmation(false, true, input, diagnostics));
            Assert.Null(await policy.PrepareFileAsync("input.mp3", output));
            Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(output));
            Assert.Single(Directory.GetFiles(directory));
        }

        [Fact]
        public async Task YesAnswersGeneralPromptsWhileOverwriteOnlyAuthorizesFiles()
        {
            string output = await ExistingFileAsync();
            using StringReader input = new("");
            using StringWriter diagnostics = new(CultureInfo.InvariantCulture);
            CommandConfirmation yes = new(true, false, input, diagnostics);
            Assert.True(await yes.ConfirmAsync("Continue?"));
            CommandOutputPolicy yesPolicy = new(false, yes);
            Assert.NotNull(await yesPolicy.PrepareFileAsync("input.mp3", output));
            CommandConfirmation normal = new(false, false, input, diagnostics);
            CommandOutputPolicy overwritePolicy = new(true, normal);
            Assert.NotNull(await overwritePolicy.PrepareFileAsync("input.mp3", output));
            Assert.False(await normal.ConfirmAsync("Continue?"));
            Assert.Equal(string.Empty, diagnostics.ToString());
        }

        [Theory]
        [InlineData("-y")]
        [InlineData("--overwrite")]
        public async Task InvalidMediaDoesNotReplaceAnApprovedDestination(string option)
        {
            string output = await ExistingFileAsync();
            string input = Path.Combine(directory, "invalid.mp3");
            await File.WriteAllTextAsync(input, "invalid media");
            (int code, _, _) = await CliTests.RunAsync(["-i", input, output, option]);
            Assert.NotEqual(0, code);
            Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(output));
            Assert.Equal(2, Directory.GetFiles(directory).Length);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task WriterFailureAndCancellationPreserveApprovedDestination(bool cancel)
        {
            string output = await ExistingFileAsync();
            using CancellationTokenSource cancellation = new();
            CommandOutputFile file = new(output, overwrite: true);
            async Task WriteAsync(Stream stream)
            {
                await stream.WriteAsync(new byte[] { 7, 8 });
                if (cancel)
                {
                    await cancellation.CancelAsync();
                    return;
                }
                throw new IOException("Writing failed.");
            }
            if (cancel)
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => file.WriteAsync(WriteAsync, cancellation.Token));
            }
            else
            {
                await Assert.ThrowsAsync<IOException>(() => file.WriteAsync(WriteAsync));
            }
            Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(output));
            Assert.Single(Directory.GetFiles(directory));
        }

        [Fact]
        public async Task DestinationCreatedAfterPreparationRequiresExplicitOverwriteApproval()
        {
            string output = Path.Combine(directory, "raced.wav");
            using StringReader input = new("");
            using StringWriter diagnostics = new(CultureInfo.InvariantCulture);
            CommandOutputPolicy policy = new(false, new CommandConfirmation(false, false, input, diagnostics));
            CommandOutputFile file = Assert.IsType<CommandOutputFile>(await policy.PrepareFileAsync("input.mp3", output));
            await File.WriteAllBytesAsync(output, [1, 2, 3]);
            await Assert.ThrowsAsync<IOException>(() => file.WriteAsync(stream => stream.WriteAsync(new byte[] { 7, 8 }).AsTask()));
            Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(output));
            Assert.Single(Directory.GetFiles(directory));
        }

        [Fact]
        public async Task ForceDoesNotAllowTheInputPathOrAnExistingDirectoryAsFileOutput()
        {
            string input = Path.Combine(directory, "input.wav");
            byte[] original = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Wave", "s16-stereo.wav"));
            await File.WriteAllBytesAsync(input, original);
            (int sameCode, _, _) = await CliTests.RunAsync(["-i", input, input, "-y"]);
            Assert.Equal(2, sameCode);
            Assert.Equal(original, await File.ReadAllBytesAsync(input));
            string output = Path.Combine(directory, "output.wav");
            Directory.CreateDirectory(output);
            (int directoryCode, _, _) = await CliTests.RunAsync(["-i", input, output, "-y"]);
            Assert.Equal(5, directoryCode);
            Assert.Empty(Directory.GetFileSystemEntries(output));
        }

        private async Task<string> ExistingFileAsync()
        {
            string path = Path.Combine(directory, "existing.wav");
            await File.WriteAllBytesAsync(path, [1, 2, 3]);
            return path;
        }

        public void Dispose() => Directory.Delete(directory, recursive: true);
    }
}
