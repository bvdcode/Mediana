// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.Mp4;
using Mediana.Containers.MpegTs;
using Mediana.Core;

namespace Mediana.Streaming.Hls
{
    internal static class Mp4HlsPacketWriter
    {
        public static async ValueTask CopyAsync(Mp4Reader reader, int videoStreamIndex, Mp4HlsSegment segment,
            MpegTsWriter writer, CancellationToken cancellationToken)
        {
            int video = segment.Video.FirstSample;
            int videoEnd = checked(video + segment.Video.SampleCount);
            int audio = segment.Audio?.FirstSample ?? 0;
            int audioEnd = checked(audio + (segment.Audio?.SampleCount ?? 0));
            Mp4SampleInfo? videoInfo = video < videoEnd ? reader.GetSampleInfo(videoStreamIndex, video, cancellationToken) : null;
            Mp4SampleInfo? audioInfo = audio < audioEnd ? reader.GetSampleInfo(segment.Audio!.Value.StreamIndex, audio, cancellationToken) : null;
            while (videoInfo.HasValue || audioInfo.HasValue)
            {
                cancellationToken.ThrowIfCancellationRequested();
                bool selectVideo = videoInfo.HasValue &&
                    (!audioInfo.HasValue || videoInfo.Value.DecodeTime.CompareTo(audioInfo.Value.DecodeTime) <= 0);
                Mp4SampleInfo selected = selectVideo ? videoInfo!.Value : audioInfo!.Value;
                using (MediaPacket packet = await reader.ReadPacketAtAsync(selected.StreamIndex, selected.SampleIndex, cancellationToken).ConfigureAwait(false))
                {
                    await writer.WritePacketAsync(packet, cancellationToken).ConfigureAwait(false);
                }
                if (selectVideo)
                {
                    video++;
                    videoInfo = video < videoEnd ? reader.GetSampleInfo(videoStreamIndex, video, cancellationToken) : null;
                }
                else
                {
                    audio++;
                    audioInfo = audio < audioEnd ? reader.GetSampleInfo(segment.Audio!.Value.StreamIndex, audio, cancellationToken) : null;
                }
            }
        }
    }
}
