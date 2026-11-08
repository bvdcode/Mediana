// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Timing;

namespace Mediana.Containers.Matroska
{
    internal partial class MatroskaPacketSource
    {
        private MatroskaPresentationIndex? presentation;
        private MatroskaAudioTimingIndex? audioTiming;

        public async ValueTask InitializePresentationAsync(MatroskaIndexOptions options, CancellationToken token)
        {
            audioTiming = await MatroskaAudioTimingIndex.CreateAsync(source, allocator, index, tracks,
                configurations, options, token).ConfigureAwait(false);
            presentation = await MatroskaPresentationIndex.CreateAsync(source, allocator, index, this,
                configurations, options, token).ConfigureAwait(false);
        }

        public long GetPresentationUnitCount(int stream)
        {
            EnsurePresentationStream(stream);
            return presentation!.GetCount(stream);
        }

        public ContainerVideoPresentationUnit GetPresentationUnit(int stream, long unit, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            EnsurePresentationStream(stream);
            return presentation!.GetUnit(stream, unit);
        }

        public MediaDuration? GetPresentationDuration(int stream, long packet, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            MatroskaPacketAddress address = index.Locate(stream, packet);
            return configurations[stream].Stream.Kind == MediaKind.Video ? presentation!.GetDuration(stream, packet) : GetTiming(address).Duration;
        }

        internal MediaDuration? GetExplicitPresentationDuration(MatroskaPacketAddress address)
        {
            MatroskaIndexedBlock block = index.GetBlock(address.BlockIndex);
            if (index.GetExtensions(address.BlockIndex)?.DurationTicks is not ulong ticks) { return null; }
            MediaDuration whole = clocks[block.StreamIndex].GetTrackDuration(ticks);
            if (block.FrameCount == 1) { return whole; }
            if (framePeriods[block.StreamIndex] is not MediaDuration period) { return null; }
            MediaTimestamp preceding = new(checked(period.Value * (block.FrameCount - 1)), period.TimeBase);
            MediaTimestamp end = new(whole.Value, whole.TimeBase);
            if (preceding.CompareTo(end) >= 0)
            { throw new InvalidDataException("The Matroska BlockDuration cannot contain its laced presentation intervals."); }
            return address.FrameIndex == block.FrameCount - 1 ? new MediaTimeRange(preceding, end).Duration : period;
        }

        private void EnsurePresentationStream(int stream)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            ArgumentOutOfRangeException.ThrowIfNegative(stream);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(stream, configurations.Length);
            if (configurations[stream].Stream.Kind != MediaKind.Video)
            { throw new ArgumentException("A video presentation index requires a video stream.", nameof(stream)); }
            if (presentation is null) { throw new InvalidOperationException("The Matroska presentation index is incomplete."); }
        }
    }
}
