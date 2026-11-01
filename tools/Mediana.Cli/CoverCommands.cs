// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using ConsoleAppFramework;

namespace Mediana.Cli
{
    internal class CoverCommands(TextWriter writer, CommandOutputPolicy outputs) : MediaCommands(outputs)
    {
        /// <summary>Extract original embedded picture bytes; the input content determines its container.</summary>
        /// <param name="input">Existing input media file.</param>
        /// <param name="output">Output file for the original picture encoding.</param>
        /// <param name="index">Zero-based picture index in container order.</param>
        [Command("")]
        public Task<int> Extract([Argument] string input, [Argument] string output, int index = 0,
            CancellationToken cancellationToken = default) => ExtractAsync(input, output, index, null, cancellationToken);

        /// <summary>Select the first recognized still image from picture tags and attachments; preserve its original encoding.</summary>
        /// <param name="input">Existing input media file; content determines the container.</param>
        /// <param name="output">Output file for original encoded image bytes.</param>
        /// <param name="first">First source to inspect: Attachment or Picture; the other source follows.</param>
        public Task<int> Select([Argument] string input, [Argument] string output, ContainerImageSource first = ContainerImageSource.Attachment,
            CancellationToken cancellationToken = default) => ExecuteFileAsync(input, output,
                file => ContainerImageCommand.RunAsync(input, file, first, writer, cancellationToken), cancellationToken);

        /// <summary>Extract an embedded cover without re-encoding. Format: Mp4.</summary>
        /// <param name="input">Existing input media file.</param>
        /// <param name="output">Output image file.</param>
        /// <param name="index">Zero-based cover index.</param>
        public Task<int> Mp4([Argument] string input, [Argument] string output, int index = 0, CancellationToken cancellationToken = default)
        {
            return ExtractAsync(input, output, index, "mp4", cancellationToken);
        }

        /// <summary>Extract an embedded cover without re-encoding. Format: Mp3.</summary>
        /// <param name="input">Existing input media file.</param>
        /// <param name="output">Output image file.</param>
        /// <param name="index">Zero-based cover index.</param>
        public Task<int> Mp3([Argument] string input, [Argument] string output, int index = 0, CancellationToken cancellationToken = default)
        {
            return ExtractAsync(input, output, index, "mp3", cancellationToken);
        }

        /// <summary>Extract an embedded cover without re-encoding. Format: Flac.</summary>
        /// <param name="input">Existing input media file.</param>
        /// <param name="output">Output image file.</param>
        /// <param name="index">Zero-based cover index.</param>
        public Task<int> Flac([Argument] string input, [Argument] string output, int index = 0, CancellationToken cancellationToken = default)
        {
            return ExtractAsync(input, output, index, "flac", cancellationToken);
        }

        private Task<int> ExtractAsync(string input, string destination, int index, string? requiredFormat, CancellationToken token)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            return ExecuteFileAsync(input, destination,
                file => ContainerPictureCommand.RunAsync(input, file, index, writer, requiredFormat, token), token);
        }
    }
}
