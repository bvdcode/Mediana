// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Codecs.Aac;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana.Cli
{
    internal class HlsAacAudioSource(HlsAacPcmSource pcm, AacEncoder encoder) : HlsAudioSource
    {
        private bool completed;
        public override EncodedStreamInfo Stream => encoder.Stream;
        public long InputSamplesPerChannel => encoder.InputSamplesPerChannel;
        public int PrimingSamples => encoder.PrimingSamples;
        public int? PaddingSamples => encoder.PaddingSamples;

        public static async ValueTask<HlsAacAudioSource> OpenAsync(IRandomAccessByteSource source, int streamIndex,
            int bitRate, int? channels, MediaBufferAllocator allocator, CancellationToken token)
        {
            HlsAacPcmSource pcm = await HlsAacPcmSource.OpenAsync(source, streamIndex, channels, allocator, token);
            bool success = false;
            try
            {
                AacEncoder encoder = AacEncoder.Create(pcm.Format, pcm.TimeBase, allocator,
                    new() { BitRate = bitRate }, streamIndex);
                HlsAacAudioSource result = new(pcm, encoder);
                success = true;
                return result;
            }
            finally
            {
                if (!success)
                {
                    await pcm.DisposeAsync();
                }
            }
        }

        protected override async ValueTask<MediaPacket?> ReadPacketAsync(CancellationToken token)
        {
            while (true)
            {
                CodecOutput<MediaPacket> result = encoder.Receive(token);
                switch (result.Status)
                {
                    case CodecOutputStatus.Available:
                        return result.Value;
                    case CodecOutputStatus.EndOfStream:
                        return null;
                    case CodecOutputStatus.InputRequired:
                        if (completed)
                        {
                            throw new InvalidOperationException("The completed AAC encoder requested more PCM.");
                        }
                        break;
                    default:
                        throw new InvalidOperationException("The AAC encoder returned an invalid output status.");
                }
                using AudioFrame? frame = await pcm.ReadFrameAsync(token);
                if (frame is null)
                {
                    encoder.CompleteInput();
                    completed = true;
                }
                else if (encoder.TrySend(frame, token) != CodecInputStatus.Accepted)
                {
                    throw new InvalidOperationException("The AAC encoder did not accept its requested PCM block.");
                }
            }
        }

        protected override async ValueTask DisposeCoreAsync()
        {
            encoder.Dispose();
            await pcm.DisposeAsync();
        }
    }
}
