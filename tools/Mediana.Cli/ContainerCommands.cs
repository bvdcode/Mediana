// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using ConsoleAppFramework;

namespace Mediana.Cli
{
    internal class ContainerCommands : MediaCommands
    {
        /// <summary>Copy supported AVC, AAC and MP3 streams to MPEG-TS; detect input by content.</summary>
        /// <param name="input">Existing supported media file.</param>
        /// <param name="output">New MPEG-TS file.</param>
        public Task<int> Remux([Argument] string input, [Argument] string output, CancellationToken cancellationToken = default) =>
            ExecuteAsync(input, output, () => MpegTsRemuxCommand.RunAsync(input, output, cancellationToken: cancellationToken));
    }
}
