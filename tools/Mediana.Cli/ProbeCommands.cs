// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using ConsoleAppFramework;

namespace Mediana.Cli
{
    internal class ProbeCommands(TextWriter output, CommandOutputPolicy outputs) : MediaCommands(outputs)
    {
        /// <summary>Inspect media identity, image dimensions, declared duration and streams without decoding.</summary>
        /// <param name="input">Existing supported media file; its content determines the format.</param>
        [Command("")]
        public Task<int> Info([Argument] string input, CancellationToken cancellationToken = default) =>
            ExecuteAsync(input, null, () => MediaProbeCommand.RunAsync(input, output, cancellationToken));

        /// <summary>Inspect supported containers and metadata. Format: Mp4.</summary>
        /// <param name="input">Existing input media file.</param>
        public Task<int> Mp4([Argument] string input, CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(input, null, () => MediaProbeCommand.RunAsync(input, output, cancellationToken, "mp4"));
        }

        /// <summary>Inspect supported containers and metadata. Format: Mp3.</summary>
        /// <param name="input">Existing input media file.</param>
        public Task<int> Mp3([Argument] string input, CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(input, null, () => MediaProbeCommand.RunAsync(input, output, cancellationToken, "mp3"));
        }

        /// <summary>Inspect supported containers and metadata. Format: Flac.</summary>
        /// <param name="input">Existing input media file.</param>
        public Task<int> Flac([Argument] string input, CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(input, null, () => MediaProbeCommand.RunAsync(input, output, cancellationToken, "flac"));
        }
    }
}
