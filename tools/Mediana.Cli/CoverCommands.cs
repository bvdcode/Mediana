// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using ConsoleAppFramework;

namespace Mediana.Cli
{
    internal class CoverCommands(CommandOutputPolicy outputs) : MediaCommands(outputs)
    {
        /// <summary>Extract an embedded cover without re-encoding. Format: Mp4.</summary>
        /// <param name="input">Existing input media file.</param>
        /// <param name="output">Output image file.</param>
        /// <param name="index">Zero-based cover index.</param>
        public Task<int> Mp4([Argument] string input, [Argument] string output, int index = 0, CancellationToken cancellationToken = default)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            return ExecuteFileAsync(input, output, outputFile => Mp4CoverArtCommand.RunAsync(input, outputFile, index, cancellationToken), cancellationToken);
        }

        /// <summary>Extract an embedded cover without re-encoding. Format: Mp3.</summary>
        /// <param name="input">Existing input media file.</param>
        /// <param name="output">Output image file.</param>
        /// <param name="index">Zero-based cover index.</param>
        public Task<int> Mp3([Argument] string input, [Argument] string output, int index = 0, CancellationToken cancellationToken = default)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            return ExecuteFileAsync(input, output, outputFile => Mp3PictureCommand.RunAsync(input, outputFile, index, cancellationToken), cancellationToken);
        }

        /// <summary>Extract an embedded cover without re-encoding. Format: Flac.</summary>
        /// <param name="input">Existing input media file.</param>
        /// <param name="output">Output image file.</param>
        /// <param name="index">Zero-based cover index.</param>
        public Task<int> Flac([Argument] string input, [Argument] string output, int index = 0, CancellationToken cancellationToken = default)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            return ExecuteFileAsync(input, output, outputFile => FlacPictureCommand.RunAsync(input, outputFile, index, cancellationToken), cancellationToken);
        }
    }
}
