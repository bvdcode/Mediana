// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using ConsoleAppFramework;

namespace Mediana.Cli
{
    internal class ContainerCommands : MediaCommands
    {
        /// <summary>Copy supported AVC/AAC streams from MP4/MOV to MPEG-TS.</summary>
        /// <param name="input">Existing MP4/MOV file.</param>
        /// <param name="output">New MPEG-TS file.</param>
        public Task<int> Remux([Argument] string input, [Argument] string output, CancellationToken cancellationToken = default) =>
            ExecuteAsync(input, output, () => Mp4RemuxCommand.RunAsync(input, output, cancellationToken));
    }
}
