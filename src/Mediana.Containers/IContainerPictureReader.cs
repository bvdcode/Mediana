// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;

namespace Mediana.Containers
{
    /// <summary>Optional access to pictures described by inspected container metadata.</summary>
    public interface IContainerPictureReader : IContainerReader
    {
        IReadOnlyList<ContainerPictureInfo> Pictures { get; }

        /// <summary>Copies min(destination.Length, declared picture byte length) original bytes from its start.
        /// Does not load additional picture payload beyond that prefix or move packet/source cursors.
        /// The caller owns the destination; bytes after the returned count remain unchanged.</summary>
        ValueTask<int> ReadPicturePrefixAsync(int pictureIndex, Memory<byte> destination, CancellationToken cancellationToken = default);

        /// <summary>Returns owned original bytes by zero-based picture index without moving packet cursors.
        /// The lease survives reader disposal and accounts for its complete backing storage.
        /// External references are returned as data; resources are never retrieved.</summary>
        ValueTask<MediaBuffer> ReadPictureAsync(int pictureIndex, CancellationToken cancellationToken = default);
    }
}
