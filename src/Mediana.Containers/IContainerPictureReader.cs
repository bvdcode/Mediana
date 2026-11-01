// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;

namespace Mediana.Containers
{
    /// <summary>Optional access to pictures described by inspected container metadata.</summary>
    public interface IContainerPictureReader : IContainerReader
    {
        IReadOnlyList<ContainerPictureInfo> Pictures { get; }

        /// <summary>Returns owned original bytes by zero-based picture index without moving packet cursors.
        /// The lease survives reader disposal and accounts for its complete backing storage.
        /// External references are returned as data; resources are never retrieved.</summary>
        ValueTask<MediaBuffer> ReadPictureAsync(int pictureIndex, CancellationToken cancellationToken = default);
    }
}
