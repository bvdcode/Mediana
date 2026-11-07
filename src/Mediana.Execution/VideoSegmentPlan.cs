// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Collections;
using System.Runtime.InteropServices;
using Mediana.Core.Memory;
using Mediana.Core.Timing;

namespace Mediana.Execution
{
    /// <summary>Immutable frame-aligned intervals in allocator-owned storage. Dispose to release the plan;
    /// copied interval values remain usable independently of this owner and the source reader.</summary>
    public class VideoSegmentPlan : IReadOnlyList<MediaTimeRange>, IDisposable
    {
        private readonly MediaBuffer storage;

        internal VideoSegmentPlan(MediaBuffer storage, int count)
        {
            this.storage = storage.Retain();
            Count = count;
        }

        public int Count { get; }
        public MediaTimeRange this[int index]
        {
            get
            {
                ArgumentOutOfRangeException.ThrowIfNegative(index);
                ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);
                return MemoryMarshal.Cast<byte, MediaTimeRange>(storage.Memory.Span)[index];
            }
        }

        public IEnumerator<MediaTimeRange> GetEnumerator()
        {
            for (int index = 0; index < Count; index++) { yield return this[index]; }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        public void Dispose() => storage.Dispose();
    }
}
