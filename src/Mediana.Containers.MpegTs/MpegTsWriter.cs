// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;

namespace Mediana.Containers.MpegTs
{
    /// <summary>Writes one program of AVC/AAC/MP3 access units in globally nondecreasing decode order.
    /// Borrows its output stream and input packets. A failed write faults the writer; partial output must be discarded.</summary>
    public partial class MpegTsWriter : IContainerWriter
    {
        private const int MaximumStreams = 16;
        private const long PcrInterval = 3600;
        private const long TableInterval = 9000;
        private const long PcrLead = 9000;
        private static readonly TimeBase Clock = new(1, 90000);
        private Stream destination;
        private readonly MediaBufferAllocator allocator;
        private readonly MpegTsWriteOptions options;
        private readonly MpegTsTrack[] tracks;
        private readonly byte[] streamTypes;
        private readonly MpegTsTrack clockTrack;
        private readonly WritableMediaBuffer scratch;
        private readonly long timestampOffset;
        private readonly long maximumGap;
        private long? lastDecodeTime;
        private long? lastPcr;
        private long? lastTables;
        private int patCounter = 15;
        private int pmtCounter = 15;
        private int busy;
        private bool completed;
        private bool faulted;
        private bool disposed;
        private bool segmentPending;

        /// <summary>Bytes successfully written by this instance, including tables and clock packets across segment boundaries.</summary>
        public long BytesWritten { get; private set; }

        private MpegTsWriter(Stream destination, MediaBufferAllocator allocator, MpegTsWriteOptions options, MpegTsTrack[] tracks)
        {
            this.destination = destination;
            this.allocator = allocator;
            this.options = options;
            this.tracks = tracks;
            streamTypes = tracks.Select(track => track.Type).ToArray();
            clockTrack = tracks.FirstOrDefault(track => track.Type == 0x1B) ?? tracks[0];
            timestampOffset = new MediaTimestamp(options.TimestampOffset.Value, options.TimestampOffset.TimeBase)
                .Rescale(Clock, TimestampRounding.NearestEven).Value;
            maximumGap = new MediaTimestamp(options.MaximumTimestampGap.Value, options.MaximumTimestampGap.TimeBase)
                .Rescale(Clock, TimestampRounding.NearestEven).Value;
            if (maximumGap == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(options), "The maximum timestamp gap is below the TS clock resolution.");
            }
            scratch = allocator.Allocate(MpegTsPacket.Size + 19);
        }

        public static MpegTsWriter Create(Stream destination, IReadOnlyList<EncodedStreamInfo> streams,
            MediaBufferAllocator allocator, MpegTsWriteOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(destination);
            ArgumentNullException.ThrowIfNull(streams);
            ArgumentNullException.ThrowIfNull(allocator);
            if (!destination.CanWrite)
            {
                throw new ArgumentException("The TS destination must be writable.", nameof(destination));
            }
            options ??= new();
            options.Validate();
            if (streams.Count is < 1 or > MaximumStreams)
            {
                throw new NotSupportedException("The TS writer requires one to sixteen streams.");
            }
            HashSet<int> indices = [];
            MpegTsTrack[] tracks = new MpegTsTrack[streams.Count];
            for (int index = 0; index < streams.Count; index++)
            {
                EncodedStreamInfo stream = streams[index];
                ArgumentNullException.ThrowIfNull(stream);
                if (!indices.Add(stream.StreamIndex))
                {
                    throw new ArgumentException("TS stream indices must be unique.", nameof(streams));
                }
                if (stream.Configuration.Length > options.MaximumConfigurationBytes)
                {
                    throw new NotSupportedException("The stream configuration exceeds the TS byte limit.");
                }
                tracks[index] = new(stream, index);
            }
            return new(destination, allocator, options, tracks);
        }

        public async ValueTask WritePacketAsync(MediaPacket packet, CancellationToken cancellationToken = default)
        {
            Enter();
            bool success = false;
            try
            {
                EnsureWritable();
                ArgumentNullException.ThrowIfNull(packet);
                cancellationToken.ThrowIfCancellationRequested();
                MpegTsTrack track = tracks.FirstOrDefault(candidate => candidate.StreamIndex == packet.StreamIndex)
                    ?? throw new ArgumentException("The packet stream is not configured in this TS program.", nameof(packet));
                if (packet.Data.Length > options.MaximumPacketBytes)
                {
                    throw new NotSupportedException("The input packet exceeds the TS byte limit.");
                }
                long pts = Timestamp(packet.PresentationTime);
                long dts = Timestamp(packet.DecodeTime);
                if (pts < dts || dts < PcrLead || track.LastDecodeTime >= dts || lastDecodeTime > dts ||
                    (lastDecodeTime.HasValue && dts - lastDecodeTime.Value > maximumGap))
                {
                    throw new InvalidDataException("TS packets require ordered decode times, PTS >= DTS and a sufficient timeline offset without excessive gaps.");
                }
                using MediaBuffer framed = track.Frame(packet.Data.Span, allocator, options.MaximumPacketBytes, out bool idr);
                if (track.Type == 0x1B && !track.LastDecodeTime.HasValue && !idr)
                {
                    throw new InvalidDataException("Each AVC track must start with an IDR access unit.");
                }
                if (segmentPending && (track != clockTrack || !idr))
                {
                    throw new InvalidDataException("A new TS segment must start with the program's AVC IDR access unit.");
                }
                long clock = dts - PcrLead;
                if (!lastTables.HasValue || segmentPending)
                {
                    await WriteTablesAsync(clock, cancellationToken).ConfigureAwait(false);
                }
                if (!lastPcr.HasValue && track != clockTrack)
                {
                    await WriteClockAsync(clock, cancellationToken).ConfigureAwait(false);
                }
                while (lastPcr.HasValue && clock - lastPcr.Value > PcrInterval)
                {
                    await WriteClockAsync(lastPcr.Value + PcrInterval, cancellationToken).ConfigureAwait(false);
                }
                if (!segmentPending && ((lastTables.HasValue && clock - lastTables.Value >= TableInterval) || idr))
                {
                    await WriteTablesAsync(clock, cancellationToken).ConfigureAwait(false);
                }
                long? pcr = track == clockTrack && (!lastPcr.HasValue || clock > lastPcr.Value) ? clock : null;
                bool randomAccess = idr && (track != clockTrack || pcr.HasValue);
                int headerLength = MpegTsPes.Write(scratch.Memory.Span[MpegTsPacket.Size..], track.PesStreamId,
                    framed.Memory.Length, pts, dts, track.Type == 0x1B);
                int offset = 0;
                int total = checked(headerLength + framed.Memory.Length);
                while (offset < total)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    track.Counter = (track.Counter + 1) & 15;
                    int count = FillPesPacket(track, framed.Memory.Span, headerLength, offset, pcr, randomAccess);
                    await WriteScratchAsync(cancellationToken).ConfigureAwait(false);
                    if (pcr.HasValue)
                    {
                        lastPcr = pcr.Value;
                    }
                    pcr = null;
                    randomAccess = false;
                    offset += count;
                }
                track.LastDecodeTime = dts;
                lastDecodeTime = dts;
                segmentPending = false;
                success = true;
            }
            finally
            {
                FinishOperation(success);
            }
        }

        public async ValueTask CompleteAsync(CancellationToken cancellationToken = default)
        {
            Enter();
            bool success = false;
            try
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (!completed)
                {
                    EnsureWritable();
                    if (!lastDecodeTime.HasValue || segmentPending)
                    {
                        throw new InvalidOperationException("An empty TS program cannot be completed.");
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
                    completed = true;
                    scratch.Dispose();
                }
                success = true;
            }
            finally
            {
                FinishOperation(success);
            }
        }

        public ValueTask DisposeAsync()
        {
            Enter();
            disposed = true;
            scratch.Dispose();
            Volatile.Write(ref busy, 0);
            GC.SuppressFinalize(this);
            return ValueTask.CompletedTask;
        }

        private int FillPesPacket(MpegTsTrack track, ReadOnlySpan<byte> data, int headerLength, int offset, long? pcr, bool randomAccess)
        {
            Span<byte> payload = stackalloc byte[MpegTsPacket.PayloadSize];
            int capacity = MpegTsPacket.PayloadSize - (pcr.HasValue ? 8 : randomAccess ? 2 : 0);
            int count = Math.Min(capacity, headerLength + data.Length - offset);
            int headerBytes = Math.Clamp(headerLength - offset, 0, count);
            if (headerBytes != 0)
            {
                scratch.Memory.Span.Slice(MpegTsPacket.Size + offset, headerBytes).CopyTo(payload);
            }
            data.Slice(Math.Max(0, offset - headerLength), count - headerBytes).CopyTo(payload[headerBytes..]);
            return MpegTsPacket.Payload(scratch.Memory.Span[..MpegTsPacket.Size], track.Pid, track.Counter,
                offset == 0, payload[..count], pcr, randomAccess);
        }

        private async ValueTask WriteTablesAsync(long clock, CancellationToken cancellationToken)
        {
            patCounter = (patCounter + 1) & 15;
            MpegTsTables.Pat(scratch.Memory.Span[..MpegTsPacket.Size], patCounter);
            await WriteScratchAsync(cancellationToken).ConfigureAwait(false);
            pmtCounter = (pmtCounter + 1) & 15;
            MpegTsTables.Pmt(scratch.Memory.Span[..MpegTsPacket.Size], pmtCounter, clockTrack.Pid, streamTypes);
            await WriteScratchAsync(cancellationToken).ConfigureAwait(false);
            lastTables = clock;
        }

        private async ValueTask WriteClockAsync(long clock, CancellationToken cancellationToken)
        {
            if (!lastTables.HasValue || clock - lastTables.Value >= TableInterval)
            {
                await WriteTablesAsync(clock, cancellationToken).ConfigureAwait(false);
            }
            MpegTsPacket.Clock(scratch.Memory.Span[..MpegTsPacket.Size], clockTrack.Pid, clockTrack.Counter, clock);
            await WriteScratchAsync(cancellationToken).ConfigureAwait(false);
            lastPcr = clock;
        }

        private async ValueTask WriteScratchAsync(CancellationToken cancellationToken)
        {
            await destination.WriteAsync(scratch.Memory[..MpegTsPacket.Size], cancellationToken).ConfigureAwait(false);
            BytesWritten = checked(BytesWritten + MpegTsPacket.Size);
        }

        private long Timestamp(MediaTimestamp? value) => checked((value ?? throw new InvalidDataException("TS packets require explicit PTS and DTS."))
            .Rescale(Clock, TimestampRounding.NearestEven).Value + timestampOffset);

        private void EnsureWritable()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (completed || faulted)
            {
                throw new InvalidOperationException("The TS writer is completed or faulted.");
            }
        }

        private void Enter()
        {
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
            {
                throw new InvalidOperationException("Concurrent TS writer operations are not supported.");
            }
        }

        private void FinishOperation(bool success)
        {
            if (!success && !completed && !disposed)
            {
                faulted = true;
                scratch.Dispose();
            }
            Volatile.Write(ref busy, 0);
        }
    }
}
