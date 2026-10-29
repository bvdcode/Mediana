// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Timing;

namespace Mediana.Streaming.Hls
{
    /// <summary>An immutable video boundary plan from MP4 metadata and inspected AVC IDR headers.
    /// Preserves reader cursors. Does not reconstruct pictures, mux segments or calculate audio coverage.</summary>
    public class Mp4AvcSegmentPlan
    {
        private Mp4AvcSegmentPlan(EncodedStreamInfo videoStream, Mp4AvcSegment[] segments)
        {
            VideoStream = videoStream;
            Segments = Array.AsReadOnly(segments);
        }

        public EncodedStreamInfo VideoStream { get; }
        public IReadOnlyList<Mp4AvcSegment> Segments { get; }

        public static async ValueTask<Mp4AvcSegmentPlan> CreateAsync(Mp4Reader reader, int videoStreamIndex,
            HlsPlanningOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(reader);
            ArgumentOutOfRangeException.ThrowIfNegative(videoStreamIndex);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(videoStreamIndex, reader.Streams.Count);
            options ??= new();
            options.Validate();
            cancellationToken.ThrowIfCancellationRequested();
            if (reader.TimestampMode != Mp4TimestampMode.LinearMovie)
            {
                throw new ArgumentException("MP4 segment planning requires the linear movie timestamp mode.", nameof(reader));
            }
            EncodedStreamInfo stream = reader.Streams[videoStreamIndex];
            H264RandomAccessInspector inspector = new(stream);
            int sampleCount = reader.Tracks[videoStreamIndex].SampleCount;
            if (sampleCount == 0)
            {
                throw new NotSupportedException("AVC segment planning requires a nonempty video track.");
            }
            long interval = new MediaTimestamp(options.TargetDuration.Value, options.TargetDuration.TimeBase)
                .Rescale(stream.TimeBase, TimestampRounding.Ceiling).Value;
            List<Mp4AvcSegment> segments = [];
            Mp4SampleInfo first = reader.GetSampleInfo(videoStreamIndex, 0, cancellationToken);
            Mp4SampleInfo previous = first;
            long presentationEnd = first.PresentationTime.Value;
            for (int sample = 0; sample < sampleCount; sample++)
            {
                Mp4SampleInfo info = reader.GetSampleInfo(videoStreamIndex, sample, cancellationToken);
                if (info.Duration.Value == 0 || (sample > 0 && info.DecodeTime.CompareTo(previous.DecodeTime) <= 0))
                {
                    throw new NotSupportedException("AVC segment planning requires positive sample durations and increasing decode timestamps.");
                }
                bool idr = false;
                if (sample == 0 || info.IsSync)
                {
                    using MediaPacket packet = await reader.ReadPacketAtAsync(videoStreamIndex, sample, cancellationToken).ConfigureAwait(false);
                    idr = inspector.IsIdr(packet, cancellationToken);
                }
                if (sample == 0 && !idr)
                {
                    throw new NotSupportedException("The AVC video track must begin with an IDR picture.");
                }
                if (idr && (Int128)info.PresentationTime.Value - first.PresentationTime.Value >= interval)
                {
                    if (presentationEnd > info.PresentationTime.Value)
                    {
                        throw new NotSupportedException("Video presentation intervals overlap an IDR segment boundary.");
                    }
                    Add(segments, first, sample, info.PresentationTime.Value, info.DecodeTime, options.MaximumSegments);
                    first = info;
                    presentationEnd = first.PresentationTime.Value;
                }
                if (info.PresentationTime.CompareTo(first.PresentationTime) < 0)
                {
                    throw new NotSupportedException("A leading picture precedes its IDR segment presentation start.");
                }
                presentationEnd = Math.Max(presentationEnd, checked(info.PresentationTime.Value + info.Duration.Value));
                previous = info;
            }
            MediaTimestamp decodeEnd = new(checked(previous.DecodeTime.Value + previous.Duration.Value), stream.TimeBase);
            Add(segments, first, sampleCount, presentationEnd, decodeEnd, options.MaximumSegments);
            cancellationToken.ThrowIfCancellationRequested();
            return new(stream, segments.ToArray());
        }

        private static void Add(List<Mp4AvcSegment> segments, Mp4SampleInfo first, int endSample,
            long presentationEnd, MediaTimestamp decodeEnd, int maximumSegments)
        {
            if (segments.Count >= maximumSegments)
            {
                throw new NotSupportedException("The video segment count exceeds the configured limit.");
            }
            MediaDuration duration = new(checked(presentationEnd - first.PresentationTime.Value), first.PresentationTime.TimeBase);
            segments.Add(new(segments.Count, first.SampleIndex, endSample - first.SampleIndex,
                first.PresentationTime, duration, first.DecodeTime, decodeEnd));
        }
    }
}
