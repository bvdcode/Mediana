// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Aac;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Execution;
using Mediana.IO;

namespace Mediana.Cli
{
    internal class HlsAacAudioSource(HlsAacPcmSource pcm, HlsAacPacketBuffer[] tracks, int[] variantTracks) : HlsAudioSource
    {
        private readonly MediaEncodeGroup<AudioFrame> group = new(tracks.Select(track =>
            new MediaEncodePipeline<AudioFrame>(track.Encoder, track)).ToArray());
        private bool completed;
        private bool disposed;
        private bool faulted;
        private int busy;
        public override EncodedStreamInfo Stream => tracks[0].Encoder.Stream;
        public override long PacketsWritten { get; protected set; }
        public long InputSamplesPerChannel => tracks[0].Encoder.InputSamplesPerChannel;
        public int PrimingSamples => tracks[0].Encoder.PrimingSamples;
        public int? PaddingSamples => tracks[0].Encoder.PaddingSamples;
        public int EncoderCount => tracks.Length;
        public override EncodedStreamInfo GetStream(int variantIndex) => tracks[variantTracks[variantIndex]].Encoder.Stream;

        public static async ValueTask<HlsAacAudioSource> OpenAsync(IRandomAccessByteSource source, int streamIndex,
            IReadOnlyList<int> bitRates, int? channels, MediaBufferAllocator allocator, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(bitRates);
            int[] settings = bitRates.ToArray();
            if (settings.Length == 0)
            {
                throw new ArgumentException("Select at least one AAC variant bit rate.", nameof(bitRates));
            }
            HlsAacPcmSource pcm = await HlsAacPcmSource.OpenAsync(source, streamIndex, channels, allocator, token);
            List<HlsAacPacketBuffer> tracks = [];
            Dictionary<int, int> indices = [];
            int[] mapping = new int[settings.Length];
            bool success = false;
            try
            {
                for (int variant = 0; variant < settings.Length; variant++)
                {
                    token.ThrowIfCancellationRequested();
                    int rate = settings[variant];
                    if (!indices.TryGetValue(rate, out int index))
                    {
                        AacEncoder encoder = AacEncoder.Create(pcm.Format, pcm.TimeBase, allocator,
                            new() { BitRate = rate }, streamIndex);
                        index = tracks.Count;
                        tracks.Add(new(encoder));
                        indices.Add(rate, index);
                    }
                    mapping[variant] = index;
                }
                HlsAacAudioSource result = new(pcm, tracks.ToArray(), mapping);
                success = true;
                return result;
            }
            finally
            {
                if (!success)
                {
                    foreach (HlsAacPacketBuffer track in tracks)
                    {
                        await track.DisposeAsync();
                    }
                    await pcm.DisposeAsync();
                }
            }
        }

        public override async ValueTask WriteBeforeAsync(MediaTimestamp? boundary,
            IReadOnlyList<HlsVideoRendition> renditions, CancellationToken token)
        {
            Enter();
            bool success = false;
            try
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (faulted)
                {
                    throw new InvalidOperationException("The HLS audio encoding group has failed.");
                }
                if (renditions.Count != 0 && renditions.Count != variantTracks.Length)
                {
                    throw new ArgumentException("Supply every configured video variant.", nameof(renditions));
                }
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    for (int index = 0; index < tracks.Length; index++)
                    {
                        while (tracks[index].Next is MediaPacket next && (boundary is null
                            || next.DecodeTime!.Value.CompareTo(boundary.Value) < 0))
                        {
                            using MediaPacket packet = tracks[index].Take();
                            for (int variant = 0; variant < renditions.Count; variant++)
                            {
                                if (variantTracks[variant] == index)
                                {
                                    await renditions[variant].Writer.WritePacketAsync(packet, token);
                                }
                            }
                            PacketsWritten = checked(PacketsWritten + 1);
                        }
                    }
                    if (completed || (boundary is not null && tracks.All(track => track.Next is not null)))
                    {
                        success = true;
                        return;
                    }
                    using AudioFrame? frame = await pcm.ReadFrameAsync(token);
                    if (frame is null)
                    {
                        await group.CompleteInputAsync(token);
                        foreach (HlsAacPacketBuffer track in tracks)
                        {
                            await track.CompleteAsync(token);
                        }
                        completed = true;
                    }
                    else
                    {
                        await group.WriteAsync(frame, token);
                    }
                }
            }
            finally
            {
                faulted |= !success;
                Volatile.Write(ref busy, 0);
            }
        }

        public override async ValueTask DisposeAsync()
        {
            Enter();
            try
            {
                if (!disposed)
                {
                    disposed = true;
                    foreach (HlsAacPacketBuffer track in tracks)
                    {
                        await track.DisposeAsync();
                    }
                    await pcm.DisposeAsync();
                }
            }
            finally
            {
                Volatile.Write(ref busy, 0);
            }
            GC.SuppressFinalize(this);
        }

        private void Enter()
        {
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
            {
                throw new InvalidOperationException("Concurrent HLS audio encoding operations are not supported.");
            }
        }
    }
}
