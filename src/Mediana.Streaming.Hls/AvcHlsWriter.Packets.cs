// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Timing;

namespace Mediana.Streaming.Hls
{
    public partial class AvcHlsWriter
    {
        public async ValueTask WritePacketAsync(MediaPacket packet, CancellationToken cancellationToken = default)
        {
            output.Enter();
            bool success = false;
            try
            {
                output.EnsureWritable();
                ArgumentNullException.ThrowIfNull(packet);
                cancellationToken.ThrowIfCancellationRequested();
                bool video = packet.StreamIndex == stream.StreamIndex;
                if ((!video && packet.StreamIndex != audio?.Stream.StreamIndex) || packet.PresentationTime is not MediaTimestamp pts ||
                    packet.DecodeTime is not MediaTimestamp dts || pts != dts || packet.Duration is not MediaDuration duration || duration.Value == 0)
                {
                    throw new NotSupportedException("AVC HLS writing requires a configured stream, known PTS = DTS and a positive duration.");
                }
                long start = HlsClock.ExactTicks(pts, clock);
                long length = HlsClock.ExactTicks(new(duration.Value, duration.TimeBase), clock);
                long end = checked(start + length);
                if ((video ? lastEnd : audioEnd) > start || lastDecodeTime > start ||
                    (video && lastWasAudio && lastDecodeTime == start))
                {
                    throw new InvalidDataException("HLS access units require nonoverlapping track timing and global decode order with video first at equal DTS.");
                }
                if (video)
                {
                    bool first = lastEnd is null;
                    bool boundary = first || new MediaTimestamp(checked(start - output.SegmentStart), clock)
                        .CompareTo(new(options.TargetDuration.Value, options.TargetDuration.TimeBase)) >= 0;
                    if (boundary)
                    {
                        bool idr = inspector.IsIdr(packet, cancellationToken);
                        if (first && !idr)
                        {
                            throw new InvalidDataException("The first AVC HLS video packet must contain an IDR access unit.");
                        }
                        if (idr && (!first || !output.HasOutput))
                        {
                            await output.BeginAsync(start, cancellationToken).ConfigureAwait(false);
                        }
                        if (first)
                        {
                            output.SegmentStart = start;
                        }
                    }
                }
                else if (!output.HasOutput)
                {
                    await output.BeginAsync(start, cancellationToken).ConfigureAwait(false);
                }
                await output.WritePacketAsync(packet, cancellationToken).ConfigureAwait(false);
                if (video)
                {
                    lastEnd = end;
                }
                else
                {
                    audioEnd = end;
                }
                lastDecodeTime = start;
                lastWasAudio = !video;
                success = true;
            }
            finally
            {
                output.FinishOperation(success);
            }
        }
    }
}
