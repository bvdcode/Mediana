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
                TimeBase trackClock = video ? videoClock : audioClock;
                long start = HlsClock.ExactTicks(pts, trackClock);
                long length = HlsClock.ExactTicks(new(duration.Value, duration.TimeBase), trackClock);
                MediaTimestamp end = new(checked(start + length), trackClock);
                if ((video ? lastEnd : audioEnd) is MediaTimestamp priorEnd && priorEnd.CompareTo(pts) > 0 ||
                    lastDecodeTime is MediaTimestamp priorDecode && !Follows(priorDecode, pts, video))
                {
                    throw new InvalidDataException("HLS access units require nonoverlapping track timing and global decode order with video first at equal DTS.");
                }
                if (video)
                {
                    bool first = lastEnd is null;
                    bool boundary = first || new MediaTimestamp(checked(start - videoSegmentStart), trackClock)
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
                            await output.BeginAsync(SegmentTicks(pts), cancellationToken).ConfigureAwait(false);
                            videoSegmentStart = start;
                        }
                        if (first)
                        {
                            videoSegmentStart = start;
                            output.SegmentStart = SegmentTicks(pts);
                        }
                    }
                }
                else if (!output.HasOutput)
                {
                    await output.BeginAsync(SegmentTicks(pts), cancellationToken).ConfigureAwait(false);
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
                lastDecodeTime = pts;
                lastWasAudio = !video;
                success = true;
            }
            finally
            {
                output.FinishOperation(success);
            }
        }

        private bool Follows(MediaTimestamp previous, MediaTimestamp current, bool video)
        {
            int order = previous.CompareTo(current);
            return order < 0 || order == 0 && (!video || !lastWasAudio);
        }
    }
}
