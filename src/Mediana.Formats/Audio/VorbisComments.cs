// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Metadata;

namespace Mediana.Formats.Audio
{
    /// <summary>Length-prefixed UTF-8 comments shared by Vorbis, Opus and FLAC.
    /// Codec signatures, framing and padding are handled by the enclosing format.</summary>
    public class VorbisComments
    {
        private VorbisComments(string vendor, List<MediaTag> tags)
        {
            Vendor = vendor;
            Tags = tags.AsReadOnly();
        }

        public string Vendor { get; }
        public IReadOnlyList<MediaTag> Tags { get; }

        public static VorbisComments Read(ReadOnlySpan<byte> data, out int consumed,
            int maximumTextBytes = int.MaxValue, int maximumComments = int.MaxValue,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            VorbisCommentReader reader = new(data, maximumTextBytes, maximumComments);
            List<MediaTag> tags = [];
            while (reader.TryRead(out VorbisComment comment, cancellationToken))
            {
                tags.Add(new("vorbis", comment.Name, comment.Value));
            }
            consumed = reader.ConsumedBytes;
            return new(reader.Vendor, tags);
        }
    }
}
