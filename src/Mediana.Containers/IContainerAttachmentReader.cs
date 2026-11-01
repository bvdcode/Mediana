// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;

namespace Mediana.Containers
{
    /// <summary>Optional access to original attached files, distinct from timed streams and picture tags.</summary>
    public interface IContainerAttachmentReader : IContainerReader
    {
        IReadOnlyList<ContainerAttachmentInfo> Attachments { get; }

        /// <summary>Reads one zero-based attachment in storage order without advancing packet cursors.
        /// The owned lease survives reader disposal. Reader size limits and the allocator budget apply;
        /// a declaration can describe a file larger than those limits. No attached content is executed.</summary>
        ValueTask<MediaBuffer> ReadAttachmentAsync(int attachmentIndex, CancellationToken cancellationToken = default);
    }
}
