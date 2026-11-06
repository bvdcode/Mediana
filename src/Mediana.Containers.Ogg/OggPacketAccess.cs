// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;

namespace Mediana.Containers.Ogg
{
    internal static class OggPacketAccess
    {
        public static async ValueTask<MediaBuffer> ReadAsync(OggPageCache pages, OggTrack track,
            OggIndexedPacket entry, MediaBufferAllocator allocator, CancellationToken token)
        {
            using OggPage first = await pages.ReadAsync(entry.Page, token).ConfigureAwait(false);
            if (first.Serial != track.Serial || entry.Lace >= first.Lacing.Length ||
                (entry.Lace == 0 && (first.Flags & 1) != 0) || (entry.Lace > 0 && first.Lacing.Span[entry.Lace - 1] == 255))
            {
                throw new InvalidDataException("The indexed Ogg packet does not begin at its declared boundary.");
            }
            int body = first.BodyOffset;
            for (int index = 0; index < entry.Lace; index++) { body += first.Lacing.Span[index]; }
            (int length, bool complete) = Extent(first, entry.Lace);
            if (complete)
            {
                if (length != entry.Bytes) { throw new InvalidDataException("The indexed Ogg packet length has changed."); }
                return first.Slice(body, length);
            }
            if (length >= entry.Bytes) { throw new InvalidDataException("The indexed Ogg continuation exceeds its packet."); }
            using WritableMediaBuffer storage = allocator.Allocate(entry.Bytes);
            first.Data.Slice(body, length).CopyTo(storage.Memory);
            int written = length;
            uint sequence = first.Sequence;
            long nextOffset = checked(first.Offset + first.Data.Length);
            while (true)
            {
                token.ThrowIfCancellationRequested();
                using OggPage next = await pages.ReadAsync(nextOffset, token).ConfigureAwait(false);
                nextOffset = checked(next.Offset + next.Data.Length);
                if (next.Serial != track.Serial) { continue; }
                if (next.Sequence != unchecked(sequence + 1) || (next.Flags & 1) == 0 || (next.Flags & 2) != 0)
                {
                    throw new InvalidDataException("The indexed Ogg packet continuation is inconsistent.");
                }
                sequence = next.Sequence;
                (length, complete) = Extent(next, 0);
                if (length > entry.Bytes - written) { throw new InvalidDataException("The indexed Ogg packet length has changed."); }
                next.Data.Slice(next.BodyOffset, length).CopyTo(storage.Memory[written..]);
                written += length;
                if (complete)
                {
                    if (written != entry.Bytes) { throw new InvalidDataException("The indexed Ogg packet length has changed."); }
                    return storage.Publish();
                }
                if ((next.Flags & 4) != 0) { throw new InvalidDataException("The indexed Ogg end page has an unfinished packet."); }
            }
        }

        private static (int Length, bool Complete) Extent(OggPage page, int first)
        {
            int length = 0;
            for (int lace = first; lace < page.Lacing.Length; lace++)
            {
                int size = page.Lacing.Span[lace];
                length += size;
                if (size < 255) { return (length, true); }
            }
            return (length, false);
        }
    }
}
