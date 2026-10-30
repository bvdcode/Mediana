// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Core.Metadata
{
    /// <summary>Opening codec configurations. No item is implicitly selected as active, and later changes are not scanned.</summary>
    public class VideoStreamMetadata : StreamMetadata
    {
        public VideoStreamMetadata(IEnumerable<VideoStreamConfiguration> configurations) : base(MediaKind.Video)
        {
            ArgumentNullException.ThrowIfNull(configurations);
            VideoStreamConfiguration[] snapshot = configurations.ToArray();
            if (snapshot.Length == 0 || snapshot.Any(item => item is null))
            {
                throw new ArgumentException("At least one non-null video configuration is required.", nameof(configurations));
            }
            Configurations = Array.AsReadOnly(snapshot);
        }

        public IReadOnlyList<VideoStreamConfiguration> Configurations { get; }
    }
}
