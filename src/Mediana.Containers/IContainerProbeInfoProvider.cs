// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;

namespace Mediana.Containers
{
    /// <summary>Provides additional container facts for a metadata probe without consuming its packet cursor.</summary>
    public interface IContainerProbeInfoProvider
    {
        ValueTask<(ContainerInfo Info, IReadOnlyList<EncodedStreamInfo> Streams)> ReadProbeInfoAsync(
            CancellationToken cancellationToken = default);
    }
}
