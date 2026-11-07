// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana.Containers.Avi
{
    public partial class AviReader
    {
        private const uint IndexList = 1;
        private const uint IndexKeyFrame = 0x10;
        private const uint IndexNoTime = 0x100;

        private async ValueTask ReadOldIndexAsync(AviChunk index, AviChunk movie, CancellationToken cancellationToken)
        {
            if (index.Length % 16 != 0) { throw new InvalidDataException("An AVI idx1 index contains an incomplete entry."); }
            long? origin = null;
            using WritableMediaBuffer entries = allocator.Allocate(4096);
            for (long position = index.Data; position < index.End;)
            {
                int bytes = (int)Math.Min(entries.Memory.Length, index.End - position);
                await source.ReadExactlyAtAsync(position, entries.Memory[..bytes], cancellationToken).ConfigureAwait(false);
                for (int offset = 0; offset < bytes; offset += 16)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    uint id = BinaryPrimitives.ReadUInt32LittleEndian(entries.Span[offset..]);
                    uint flags = BinaryPrimitives.ReadUInt32LittleEndian(entries.Span[(offset + 4)..]);
                    uint relative = BinaryPrimitives.ReadUInt32LittleEndian(entries.Span[(offset + 8)..]);
                    uint size = BinaryPrimitives.ReadUInt32LittleEndian(entries.Span[(offset + 12)..]);
                    if ((flags & IndexList) != 0) { continue; }
                    int streamIndex = StreamIndex(id);
                    AviTrack track = Track(streamIndex);
                    if (origin is null)
                    {
                        bool fromMovie = await MatchesOldEntryAsync(movie.Data + relative, id, size, movie, cancellationToken).ConfigureAwait(false);
                        bool fromFile = await MatchesOldEntryAsync(relative, id, size, movie, cancellationToken).ConfigureAwait(false);
                        if (fromMovie == fromFile) { throw new InvalidDataException("The AVI idx1 origin is invalid or ambiguous."); }
                        origin = fromMovie ? movie.Data : 0;
                    }
                    long data = checked(origin.Value + relative + 8);
                    RequireMovieRange(data, size, movie);
                    if (track.Index is null)
                    {
                        track.Add(data, size, id, (flags & IndexKeyFrame) != 0, (flags & IndexNoTime) != 0);
                    }
                }
                position += bytes;
            }
        }

        private async ValueTask<bool> MatchesOldEntryAsync(long offset, uint id, uint size, AviChunk movie, CancellationToken cancellationToken)
        {
            if (!InMovie(offset + 8, size, movie)) { return false; }
            await source.ReadExactlyAtAsync(offset, scratch.Memory[..8], cancellationToken).ConfigureAwait(false);
            return BinaryPrimitives.ReadUInt32LittleEndian(scratch.Span) == id
                && BinaryPrimitives.ReadUInt32LittleEndian(scratch.Span[4..]) == size;
        }

        private async ValueTask ReadOpenDmlIndexAsync(AviTrack track, AviChunk chunk, CancellationToken cancellationToken)
        {
            (int type, uint count, uint id, ulong baseOffset) = await ReadIndexHeaderAsync(track, chunk, cancellationToken).ConfigureAwait(false);
            if (type == 1)
            {
                await ReadStandardEntriesAsync(track, chunk, count, id, baseOffset, cancellationToken).ConfigureAwait(false);
                return;
            }
            using WritableMediaBuffer entries = allocator.Allocate(4096);
            for (long position = 0; position < count;)
            {
                int batch = (int)Math.Min(entries.Memory.Length / 16, count - position);
                await source.ReadExactlyAtAsync(chunk.Data + 24 + position * 16, entries.Memory[..(batch * 16)], cancellationToken).ConfigureAwait(false);
                for (int index = 0; index < batch; index++)
                {
                    ulong address = BinaryPrimitives.ReadUInt64LittleEndian(entries.Span[(index * 16)..]);
                    uint size = BinaryPrimitives.ReadUInt32LittleEndian(entries.Span[(index * 16 + 8)..]);
                    uint duration = BinaryPrimitives.ReadUInt32LittleEndian(entries.Span[(index * 16 + 12)..]);
                    if (address > long.MaxValue || size < 32) { throw new InvalidDataException("An AVI super index contains an invalid sub-index extent."); }
                    AviChunk child = await ReadChunkAsync((long)address, sourceLength, cancellationToken).ConfigureAwait(false);
                    if (child.Length + 8L != size || !IsStandardIndexId(child.Id, track.Stream.StreamIndex))
                    {
                        throw new InvalidDataException("The AVI super index disagrees with its referenced standard index.");
                    }
                    (int childType, uint childCount, uint childId, ulong childBase) = await ReadIndexHeaderAsync(track, child, cancellationToken).ConfigureAwait(false);
                    if (childType != 1 || childId != id) { throw new InvalidDataException("An AVI super index must reference standard indexes for the same data chunks."); }
                    long start = track.Time;
                    await ReadStandardEntriesAsync(track, child, childCount, childId, childBase, cancellationToken).ConfigureAwait(false);
                    if (track.Time - start != duration) { throw new InvalidDataException("An AVI super index duration disagrees with its indexed packets."); }
                }
                position += batch;
            }
        }

        private async ValueTask<(int Type, uint Count, uint Id, ulong Base)> ReadIndexHeaderAsync(AviTrack track, AviChunk chunk, CancellationToken cancellationToken)
        {
            if (chunk.Length < 24) { throw new InvalidDataException("An AVI OpenDML index header is truncated."); }
            await source.ReadExactlyAtAsync(chunk.Data, scratch.Memory[..24], cancellationToken).ConfigureAwait(false);
            ushort longs = BinaryPrimitives.ReadUInt16LittleEndian(scratch.Span);
            int subtype = scratch.Span[2];
            int type = scratch.Span[3];
            uint count = BinaryPrimitives.ReadUInt32LittleEndian(scratch.Span[4..]);
            uint id = BinaryPrimitives.ReadUInt32LittleEndian(scratch.Span[8..]);
            ulong address = BinaryPrimitives.ReadUInt64LittleEndian(scratch.Span[12..]);
            if (subtype != 0 || type is not (0 or 1)) { throw new NotSupportedException("AVI OpenDML requires a complete-chunk or super index without field subdivision."); }
            int bytes = type switch { 0 => 16, 1 => 8, _ => throw new InvalidDataException("Unknown AVI index type.") };
            if (longs * 4 != bytes || count > (chunk.Length - 24L) / bytes || StreamIndex(id) != track.Stream.StreamIndex)
            {
                throw new InvalidDataException("An AVI OpenDML index has an invalid entry width, count or stream identifier.");
            }
            if (BinaryPrimitives.ReadUInt32LittleEndian(scratch.Span[20..]) != 0 || (type == 0 && address != 0))
            {
                throw new InvalidDataException("Reserved AVI index fields must be zero.");
            }
            return (type, count, id, address);
        }

        private async ValueTask ReadStandardEntriesAsync(AviTrack track, AviChunk chunk, uint count, uint id, ulong baseOffset,
            CancellationToken cancellationToken)
        {
            if (baseOffset > long.MaxValue) { throw new InvalidDataException("An AVI index base exceeds the supported source address."); }
            using WritableMediaBuffer entries = allocator.Allocate(4096);
            for (long position = 0; position < count;)
            {
                int batch = (int)Math.Min(entries.Memory.Length / 8, count - position);
                await source.ReadExactlyAtAsync(chunk.Data + 24 + position * 8, entries.Memory[..(batch * 8)], cancellationToken).ConfigureAwait(false);
                for (int index = 0; index < batch; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    uint relative = BinaryPrimitives.ReadUInt32LittleEndian(entries.Span[(index * 8)..]);
                    uint packedSize = BinaryPrimitives.ReadUInt32LittleEndian(entries.Span[(index * 8 + 4)..]);
                    uint size = packedSize & 0x7fffffff;
                    if (relative > long.MaxValue - (long)baseOffset) { throw new InvalidDataException("An AVI index packet address overflows."); }
                    long data = (long)baseOffset + relative;
                    bool found = false;
                    foreach (AviChunk movie in movies)
                    {
                        if (InMovie(data, size, movie)) { found = true; break; }
                    }
                    if (!found) { throw new InvalidDataException("An AVI index points outside all movie lists."); }
                    track.Add(data, size, id, (packedSize & 0x80000000) == 0, false);
                }
                position += batch;
            }
        }

        private async ValueTask ScanMovieAsync(long position, long end, int depth, CancellationToken cancellationToken)
        {
            while (position < end)
            {
                AviChunk chunk = await ReadChunkAsync(position, end, cancellationToken).ConfigureAwait(false);
                if (chunk.Id == List)
                {
                    if (depth != 0 || await ReadListTypeAsync(chunk, cancellationToken).ConfigureAwait(false) != 0x20636572)
                    {
                        throw new NotSupportedException("An AVI movie list supports data chunks and one level of record lists.");
                    }
                    await ScanMovieAsync(chunk.Data + 4, chunk.End, depth + 1, cancellationToken).ConfigureAwait(false);
                }
                else if (HasStreamNumber(chunk.Id))
                {
                    AviTrack track = Track(StreamIndex(chunk.Id));
                    bool keyFrame = track.Stream.Kind == MediaKind.Audio || track.Stream.CodecId == "mjpeg";
                    bool palette = (chunk.Id >> 16) == 0x6370;
                    track.Add(chunk.Data, chunk.Length, chunk.Id, keyFrame, palette);
                }
                position = chunk.Next;
            }
        }

        private static bool HasStreamNumber(uint id) => (id & 0xff) is >= 48 and <= 57 && ((id >> 8) & 0xff) is >= 48 and <= 57;

        private static int StreamIndex(uint id)
        {
            if (!HasStreamNumber(id)) { throw new InvalidDataException("An AVI data chunk requires a two-digit stream number."); }
            return (int)((id & 0xff) - 48) * 10 + (int)(((id >> 8) & 0xff) - 48);
        }

        private static bool IsStandardIndexId(uint id, int stream)
        {
            uint digits = (uint)(48 + stream / 10) | ((uint)(48 + stream % 10) << 8);
            return id == (0x7869 | (digits << 16)) || id == (digits | 0x78690000);
        }

        private static bool InMovie(long data, uint size, AviChunk movie) => data >= movie.Data + 12 && data <= movie.End
            && size <= movie.End - data && (size & 1) <= movie.End - data - size;

        private static void RequireMovieRange(long data, uint size, AviChunk movie)
        {
            if (!InMovie(data, size, movie)) { throw new InvalidDataException("An AVI idx1 entry points outside its movie list."); }
        }
    }
}
