// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers;
using Mediana.Core;

namespace Mediana
{
    /// <summary>Immutable header metadata. Survives source/session disposal and does not retain media buffers.</summary>
    public class MediaInfo
    {
        internal MediaInfo(ContainerInfo container, IReadOnlyList<EncodedStreamInfo> streams)
        {
            Container = container;
            Streams = Array.AsReadOnly(streams.ToArray());
        }

        public ContainerInfo Container { get; }
        public IReadOnlyList<EncodedStreamInfo> Streams { get; }
    }
}
