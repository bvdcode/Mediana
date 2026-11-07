// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana.Containers.Ogg
{
    /// <summary>Forward-only Ogg page verification and packet assembly, independent of codec syntax.
    /// Borrows its source; packets may outlive the reader. Operations must not overlap.
    /// A failed read faults the reader because a sequential source cannot be rolled back.</summary>
    public class OggPacketReader : IAsyncDisposable
    {
        private readonly OggPageReader pages;
        private readonly MediaBufferAllocator allocator;
        private readonly OggReadOptions options;
        private readonly Dictionary<uint, OggLogicalStream> streams = [];
        private OggPage? page;
        private OggLogicalStream? current;
        private int lace;
        private int body;
        private int lastCompleted;
        private int link;
        private bool dataStarted;
        private bool ended;
        private bool disposed;
        private bool faulted;
        private int busy;

        public OggPacketReader(IByteSource source, MediaBufferAllocator allocator, OggReadOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(allocator);
            this.options = options ?? new();
            this.options.Validate();
            this.allocator = allocator;
            pages = new(source, allocator, this.options);
        }

        public async ValueTask<OggPacket?> ReadPacketAsync(CancellationToken cancellationToken = default)
        {
            Enter();
            bool success = false;
            try
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (faulted) { throw new InvalidOperationException("The Ogg packet reader is faulted."); }
                cancellationToken.ThrowIfCancellationRequested();
                OggPacket? result = await ReadCoreAsync(cancellationToken).ConfigureAwait(false);
                success = true;
                return result;
            }
            finally
            {
                if (!success) { faulted = true; }
                Volatile.Write(ref busy, 0);
            }
        }

        private async ValueTask<OggPacket?> ReadCoreAsync(CancellationToken cancellationToken)
        {
            while (!ended)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (page is null || lace == page.Lacing.Length)
                {
                    page?.Dispose();
                    page = null;
                    page = await pages.ReadAsync(cancellationToken).ConfigureAwait(false);
                    if (page is null)
                    {
                        if (streams.Count == 0 || streams.Values.Any(stream => !stream.Ended || stream.Parts.Count != 0))
                        {
                            throw new InvalidDataException("The Ogg source ended before all logical streams completed.");
                        }
                        ended = true;
                        return null;
                    }
                    BeginPage();
                    if (page.Lacing.Length == 0) { continue; }
                }
                OggLogicalStream state = current!;
                if (state.Parts.Count == 0)
                {
                    state.PendingPage = page.Offset;
                    state.PendingLace = lace;
                }
                int start = body;
                while (lace < page.Lacing.Length)
                {
                    int length = page.Lacing.Span[lace++];
                    body += length;
                    if (length < byte.MaxValue)
                    {
                        int partLength = body - start;
                        CheckPacketSize(state, partLength);
                        MediaBuffer data = CompletePacket(state, start, partLength);
                        long packetIndex = state.NextPacket;
                        state.NextPacket = checked(packetIndex + 1);
                        bool last = lace - 1 == lastCompleted;
                        return new(data, page.Serial, link, packetIndex,
                            last && page.Granule >= 0 ? page.Granule : null,
                            packetIndex == 0, last && (page.Flags & 4) != 0, page.Offset,
                            state.PendingPage, state.PendingLace, lace == page.Lacing.Length);
                    }
                }
                int remaining = body - start;
                CheckPacketSize(state, remaining);
                state.Parts.Add(page.Slice(start, remaining));
                state.PendingBytes += remaining;
            }
            return null;
        }

        private void BeginPage()
        {
            OggPage value = page!;
            bool beginning = (value.Flags & 2) != 0;
            bool continuation = (value.Flags & 1) != 0;
            if (beginning && streams.Count > 0 && streams.Values.All(stream => stream.Ended))
            {
                foreach (OggLogicalStream state in streams.Values) { state.Dispose(); }
                streams.Clear();
                link = checked(link + 1);
                dataStarted = false;
            }
            if (beginning)
            {
                if (dataStarted || continuation || value.Sequence != 0 || streams.ContainsKey(value.Serial))
                {
                    throw new InvalidDataException("An Ogg beginning page is misplaced or has invalid sequence/continuation fields.");
                }
                if (streams.Count == options.MaximumLogicalStreams)
                {
                    throw new NotSupportedException("The Ogg logical stream count exceeds the configured limit.");
                }
                current = new();
                streams.Add(value.Serial, current);
            }
            else
            {
                dataStarted = true;
                if (!streams.TryGetValue(value.Serial, out current) || current.Ended)
                {
                    throw new InvalidDataException("An Ogg page has no active logical stream.");
                }
            }
            if (value.Sequence != current!.NextSequence || continuation != (current.Parts.Count != 0))
            {
                throw new InvalidDataException("Ogg page sequence or packet continuation is inconsistent.");
            }
            current.NextSequence = unchecked(value.Sequence + 1);
            lastCompleted = -1;
            for (int index = 0; index < value.Lacing.Length; index++)
            {
                if (value.Lacing.Span[index] < byte.MaxValue) { lastCompleted = index; }
            }
            current.Ended = (value.Flags & 4) != 0;
            if (current.Ended && ((value.Lacing.Length > 0 && value.Lacing.Span[^1] == byte.MaxValue) ||
                (value.Lacing.Length == 0 && current.Parts.Count != 0)))
            {
                throw new InvalidDataException("An Ogg end page leaves an unfinished packet.");
            }
            lace = 0;
            body = value.BodyOffset;
        }

        private MediaBuffer CompletePacket(OggLogicalStream state, int start, int length)
        {
            if (state.Parts.Count == 0) { return page!.Slice(start, length); }
            using WritableMediaBuffer storage = allocator.Allocate(checked(state.PendingBytes + length));
            int offset = 0;
            foreach (MediaBuffer part in state.Parts)
            {
                part.Memory.CopyTo(storage.Memory[offset..]);
                offset += part.Memory.Length;
            }
            page!.Data.Slice(start, length).CopyTo(storage.Memory[offset..]);
            MediaBuffer result = storage.Publish();
            state.Dispose();
            return result;
        }

        private void CheckPacketSize(OggLogicalStream state, int length)
        {
            if (length > options.MaximumPacketBytes - state.PendingBytes)
            {
                throw new NotSupportedException("The Ogg packet exceeds the configured byte limit.");
            }
        }

        private void Enter()
        {
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
            {
                throw new InvalidOperationException("Ogg packet reader operations must not overlap.");
            }
        }

        public ValueTask DisposeAsync()
        {
            Enter();
            try
            {
                if (!disposed)
                {
                    disposed = true;
                    page?.Dispose();
                    foreach (OggLogicalStream state in streams.Values) { state.Dispose(); }
                    streams.Clear();
                    pages.Dispose();
                }
                return ValueTask.CompletedTask;
            }
            finally { Volatile.Write(ref busy, 0); }
        }
    }
}
