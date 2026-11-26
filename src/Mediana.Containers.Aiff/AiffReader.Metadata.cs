// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Text;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Metadata;
using Mediana.IO;

namespace Mediana.Containers.Aiff
{
    public partial class AiffReader
    {
        private const uint NameChunk = 0x4E414D45;
        private readonly List<MediaTag> tags = [];
        private long packetCount;

        public async ValueTask<(ContainerInfo Info, IReadOnlyList<EncodedStreamInfo> Streams)> ReadProbeInfoAsync(
            CancellationToken cancellationToken = default)
        {
            Enter();
            try
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (faulted) { throw new InvalidOperationException("The AIFF reader has failed and must be disposed."); }
                cancellationToken.ThrowIfCancellationRequested();
                long tailLength = remaining - soundRemaining;
                if (source is not IRandomAccessByteSource random || tailLength <= soundTail + (soundChunkSize & 1))
                {
                    if (remaining == 0 && tags.Count != Info.Tags!.Count)
                    {
                        return (new ContainerInfo(Info.FormatId, Info.DeclaredDuration, tags, Info.StreamInfo!.Values), Streams);
                    }
                    return (Info, Streams);
                }

                await using RandomAccessCursorByteSource cursor = new(random, formEnd - tailLength, leaveOpen: true);
                await using AiffReader tail = new(cursor, allocator, options, scratch)
                {
                    remaining = tailLength,
                    soundTail = soundTail,
                    soundChunkSize = soundChunkSize,
                    compressed = compressed,
                    metadataBytes = metadataBytes,
                    chunks = chunks
                };
                await tail.DrainTailAsync(cancellationToken).ConfigureAwait(false);
                return (new ContainerInfo(Info.FormatId, Info.DeclaredDuration, tags.Concat(tail.tags), Info.StreamInfo!.Values), Streams);
            }
            finally { Volatile.Write(ref busy, 0); }
        }

        private async ValueTask ReadNameAsync(uint size, CancellationToken token)
        {
            string title = string.Empty;
            if (size > 0)
            {
                CountMetadata(size);
                using WritableMediaBuffer text = allocator.Allocate(checked((int)size));
                await ReadAsync(text.Memory, token).ConfigureAwait(false);
                title = Encoding.Latin1.GetString(text.Span.TrimEnd((byte)0));
            }
            tags.Add(new("aiff", "NAME", title, sourcePath: "FORM/NAME"));
            await ReadPaddingAsync(size, token).ConfigureAwait(false);
        }
    }
}
