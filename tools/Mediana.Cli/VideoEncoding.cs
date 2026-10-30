// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Codecs.H264;
using Mediana.Containers;
using Mediana.Core;
using Mediana.Core.Video;
using Mediana.Processing;

namespace Mediana.Cli
{
    internal static class VideoEncoding
    {
        public static async ValueTask WriteAsync(H264Encoder encoder, IContainerWriter writer, VideoFrame frame,
            Yuv420AreaResizer? resizer, CancellationToken token)
        {
            using VideoFrame prepared = resizer is null ? frame.Retain() : resizer.Apply(frame, token);
            if (encoder.TrySend(prepared, token) != CodecInputStatus.Accepted)
            {
                throw new InvalidOperationException("The intra encoder did not accept its next frame.");
            }
            using MediaPacket packet = encoder.Receive(token).Value
                ?? throw new InvalidOperationException("The intra encoder produced no packet.");
            await writer.WritePacketAsync(packet, token);
        }

        public static async ValueTask CompleteAsync(H264Encoder encoder, IContainerWriter writer, CancellationToken token)
        {
            encoder.CompleteInput();
            if (encoder.Receive(token).Status != CodecOutputStatus.EndOfStream)
            {
                throw new InvalidOperationException("The intra encoder retained delayed output.");
            }
            await writer.CompleteAsync(token);
        }
    }
}
