// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using ConsoleAppFramework;

namespace Mediana.Cli
{
    internal class ContainerCommands(CommandOutputPolicy outputs) : MediaCommands(outputs)
    {
        /// <summary>Copy supported AVC, AAC and MP3 streams to MPEG-TS; detect input by content.</summary>
        /// <param name="input">Existing supported media file.</param>
        /// <param name="output">Output MPEG-TS file.</param>
        public Task<int> Remux([Argument] string input, [Argument] string output, CancellationToken cancellationToken = default) =>
            ExecuteFileAsync(input, output, outputFile => MpegTsRemuxCommand.RunAsync(input, outputFile, cancellationToken: cancellationToken), cancellationToken);
    }
}
