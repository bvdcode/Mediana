// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using ConsoleAppFramework;

namespace Mediana.Cli
{
    internal class ContainerCommands(CommandOutputPolicy outputs, TextWriter console) : MediaCommands(outputs)
    {
        /// <summary>Extract one original attached file by storage index; detect the input by content.</summary>
        /// <param name="input">Existing input media file.</param>
        /// <param name="output">Destination for original attachment bytes.</param>
        /// <param name="index">Zero-based attachment index in storage order.</param>
        public Task<int> Attachment([Argument] string input, [Argument] string output, int index = 0,
            CancellationToken cancellationToken = default)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            return ExecuteFileAsync(input, output,
                outputFile => ContainerAttachmentCommand.RunAsync(input, outputFile, index, console, cancellationToken), cancellationToken);
        }

        /// <summary>Copy supported AVC, AAC and MP3 streams to MPEG-TS; detect input by content.</summary>
        /// <param name="input">Existing supported media file.</param>
        /// <param name="output">Output MPEG-TS file.</param>
        public Task<int> Remux([Argument] string input, [Argument] string output, CancellationToken cancellationToken = default) =>
            ExecuteFileAsync(input, output, outputFile => MpegTsRemuxCommand.RunAsync(input, outputFile, cancellationToken: cancellationToken), cancellationToken);
    }
}
