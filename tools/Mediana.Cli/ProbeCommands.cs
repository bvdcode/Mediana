// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using ConsoleAppFramework;

namespace Mediana.Cli
{
    internal class ProbeCommands(TextWriter output) : MediaCommands
    {
        /// <summary>Inspect container identity, declared duration and streams without decoding or a packet scan.</summary>
        /// <param name="input">Existing supported media file; its content determines the container.</param>
        [Command("")]
        public Task<int> Info([Argument] string input, CancellationToken cancellationToken = default) =>
            ExecuteAsync(input, null, () => MediaProbeCommand.RunAsync(input, output, cancellationToken));

        /// <summary>Inspect supported containers and metadata. Format: Mp4.</summary>
        /// <param name="input">Existing input media file.</param>
        public Task<int> Mp4([Argument] string input, CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(input, null, () => Mp4ProbeCommand.RunAsync(input, cancellationToken));
        }

        /// <summary>Inspect supported containers and metadata. Format: Mp3.</summary>
        /// <param name="input">Existing input media file.</param>
        public Task<int> Mp3([Argument] string input, CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(input, null, () => Mp3ProbeCommand.RunAsync(input, cancellationToken));
        }

        /// <summary>Inspect supported containers and metadata. Format: Flac.</summary>
        /// <param name="input">Existing input media file.</param>
        public Task<int> Flac([Argument] string input, CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(input, null, () => FlacProbeCommand.RunAsync(input, cancellationToken));
        }
    }
}
