// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana
{
    /// <summary>Explicit source priority for image recognition. Entries within each source retain container order.</summary>
    public record ContainerImageSearchOptions
    {
        public ContainerImageSearchOptions(IEnumerable<ContainerImageSource> sources)
        {
            ArgumentNullException.ThrowIfNull(sources);
            ContainerImageSource[] snapshot = sources.ToArray();
            if (snapshot.Length == 0)
            {
                throw new ArgumentException("At least one container image source is required.", nameof(sources));
            }
            for (int i = 0; i < snapshot.Length; i++)
            {
                switch (snapshot[i])
                {
                    case ContainerImageSource.Picture:
                    case ContainerImageSource.Attachment:
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(sources));
                }
                if (Array.IndexOf(snapshot, snapshot[i]) != i)
                {
                    throw new ArgumentException("Container image sources cannot be repeated.", nameof(sources));
                }
            }
            Sources = Array.AsReadOnly(snapshot);
        }

        public IReadOnlyList<ContainerImageSource> Sources { get; }
        /// <summary>Maximum inspected declarations, including external picture references. Exceeding it is an error.</summary>
        public int MaxCandidates { get; init; } = 256;
        public ImageOpenOptions Image { get; init; } = new();

        internal void Validate()
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxCandidates);
            ArgumentNullException.ThrowIfNull(Image);
        }
    }
}
