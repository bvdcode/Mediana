// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Formats.Video
{
    /// <summary>Replaces SPS video signal declarations while preserving all other RBSP syntax.</summary>
    public static class H264ColorDescriptionWriter
    {
        /// <summary>Returns an owned SPS NAL without a length prefix. A null source range preserves the SPS range.</summary>
        public static MediaBuffer Rewrite(ReadOnlySpan<byte> nal, CicpColorDescription source,
            MediaBufferAllocator allocator, int maximumBytes)
        {
            ArgumentNullException.ThrowIfNull(source);
            return H264SequenceDescriptionWriter.Rewrite(nal, source, null, allocator, maximumBytes);
        }
    }
}
