// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Execution;

namespace Mediana
{
    internal class MediaTrackInput(IIndexedContainerReader reader, MediaBufferAllocator allocator, MediaBufferAllocator inputAllocator,
        MediaReadBudget budget, MediaTrackResources resources, MediaTimeRange? selection)
    {
        public IIndexedContainerReader Reader { get; } = reader;
        public MediaBufferAllocator Allocator { get; } = allocator;
        public MediaBufferAllocator InputAllocator { get; } = inputAllocator;
        public MediaReadBudget Budget { get; } = budget;
        public MediaTrackResources Resources { get; } = resources;
        public MediaTimeRange? Selection { get; } = selection;

        public MediaTrackInput WithSelection(MediaTimeRange value) => new(Reader, Allocator, InputAllocator, Budget, Resources, value);

        public EncodedStreamInfo Select(int index, MediaKind? kind = null)
        {
            EncodedStreamInfo stream = Reader.Streams.SingleOrDefault(item => item.StreamIndex == index)
                ?? throw new ArgumentException("The selected input stream does not exist.", nameof(index));
            if (kind is MediaKind expected && stream.Kind != expected)
            { throw new NotSupportedException("The selected input stream has a different media kind."); }
            return stream;
        }

        public MediaTimeRange? GetSelection(int streamIndex, CancellationToken token)
        {
            MediaTimeRange? declared = (Reader as IContainerPresentationReader)?.GetPresentationRange(streamIndex, token);
            if (declared is not MediaTimeRange source) { return Selection; }
            if (Selection is not MediaTimeRange requested) { return source; }
            MediaTimestamp start = source.Start.CompareTo(requested.Start) > 0 ? source.Start : requested.Start;
            MediaTimestamp end = source.End.CompareTo(requested.End) < 0 ? source.End : requested.End;
            if (end.CompareTo(start) < 0) { end = start; }
            return new(start.ReduceClock(), end.ReduceClock());
        }
    }
}
