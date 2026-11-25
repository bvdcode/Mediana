// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Timing;
using Mediana.Formats.Audio;
using Mediana.IO;

namespace Mediana.Containers.Mp3
{
    public partial class Mp3Reader
    {
        /// <summary>Counts coded frames on known-length random-access input when Xing/Info provides no count.</summary>
        public async ValueTask<(ContainerInfo Info, IReadOnlyList<EncodedStreamInfo> Streams)> ReadProbeInfoAsync(
            CancellationToken cancellationToken = default)
        {
            Enter();
            try
            {
                RequireActive();
                cancellationToken.ThrowIfCancellationRequested();
                if (Info.DeclaredDuration is not null || source is not IRandomAccessByteSource { Length: long length } random ||
                    await CountFramesAsync(random, length, cancellationToken).ConfigureAwait(false) is not long count)
                {
                    return (Info, Streams);
                }
                MediaDuration duration = Duration(count);
                EncodedStreamInfo stream = Streams[0];
                return (CreateInfo(duration), Array.AsReadOnly(new[]
                {
                    new EncodedStreamInfo(stream.StreamIndex, stream.Kind, stream.CodecId, stream.Format, stream.TimeBase,
                        stream.Configuration.Span, codedDuration: duration)
                }));
            }
            finally
            {
                Volatile.Write(ref busy, 0);
            }
        }

        private async ValueTask<long?> CountFramesAsync(IRandomAccessByteSource random, long length, CancellationToken token)
        {
            long offset = AudioOffset + FirstFrame.FrameBytes;
            long count = 1;
            Memory<byte> header = scratch.Memory[..Mp3FrameHeader.HeaderBytes];
            while (offset != length)
            {
                token.ThrowIfCancellationRequested();
                long remaining = length - offset;
                if (remaining < header.Length)
                {
                    return null;
                }
                await random.ReadExactlyAtAsync(offset, header, token).ConfigureAwait(false);
                if (remaining == scratch.Memory.Length && header.Span[..3].SequenceEqual("TAG"u8))
                {
                    break;
                }
                if (count == options.MaxAudioFrames || FirstFrame.GetMatchingFrameBytes(header.Span) is not int frameBytes ||
                    frameBytes > options.MaxFrameBytes || frameBytes > remaining)
                {
                    return null;
                }
                offset += frameBytes;
                count++;
            }
            return count;
        }
    }
}
