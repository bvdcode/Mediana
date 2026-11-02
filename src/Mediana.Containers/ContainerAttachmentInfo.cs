// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers
{
    /// <summary>Immutable declarations for an attached file. Names and media types are untrusted metadata;
    /// they do not validate content or select a destination path.</summary>
    public class ContainerAttachmentInfo
    {
        public ContainerAttachmentInfo(long byteLength, string? fileName = null, string? mediaType = null,
            string? description = null)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(byteLength);
            ByteLength = byteLength;
            FileName = fileName;
            MediaType = mediaType;
            Description = description;
        }

        public long ByteLength { get; }
        public string? FileName { get; }
        public string? MediaType { get; }
        public string? Description { get; }
    }
}
