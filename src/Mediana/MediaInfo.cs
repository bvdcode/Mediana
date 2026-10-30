// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Collections.ObjectModel;
using Mediana.Containers;
using Mediana.Core;
using Mediana.Core.Metadata;

namespace Mediana
{
    /// <summary>Immutable header metadata. Survives source/session disposal and does not retain media buffers.</summary>
    public class MediaInfo
    {
        internal MediaInfo(ContainerInfo container, IReadOnlyList<EncodedStreamInfo> streams, Dictionary<int, StreamMetadata> streamMetadata)
        {
            Container = container;
            Streams = Array.AsReadOnly(streams.ToArray());
            StreamMetadata = new ReadOnlyDictionary<int, StreamMetadata>(streamMetadata);
        }

        internal MediaInfo(ImageInfo image)
        {
            Image = image;
            Streams = Array.Empty<EncodedStreamInfo>();
            StreamMetadata = ReadOnlyDictionary<int, StreamMetadata>.Empty;
        }

        /// <summary>Container metadata, or null for an image/document input.</summary>
        public ContainerInfo? Container { get; }
        /// <summary>Image/document metadata, or null for a packet container. Exactly one representation is present.</summary>
        public ImageInfo? Image { get; }
        public IReadOnlyList<EncodedStreamInfo> Streams { get; }
        /// <summary>Codec header facts keyed by stream index. Missing entries mean no reader is registered
        /// or the registered reader does not support that configuration.</summary>
        public IReadOnlyDictionary<int, StreamMetadata> StreamMetadata { get; }
    }
}
