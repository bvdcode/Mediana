// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;

using Mediana.Formats.Audio;

namespace Mediana.Codecs.Aac
{
    internal partial class AacDecoder : IMediaCodec<MediaPacket, AudioFrame>, IPacketPrerollDecoder
    {
        private const uint InitialNoiseState = 0x13579bdf;
        private readonly int streamIndex;
        private readonly AacConfiguration configuration;
        private readonly MediaBufferAllocator allocator;
        private readonly int maxAccessUnitBytes;
        private AudioFormat format;
        private readonly AudioFormat coreFormat;
        private AudioFormat? sbrFormat;
        private readonly AacChannelLayout layout;
        private AacDecoderWorkspace? workspace;
        private readonly bool adts;
        private readonly AudioFrame?[] pending;
        private int outputCount;
        private int outputIndex;
        private long prerollPackets;
        private long prerollAccessUnits;
        private uint noiseState = InitialNoiseState;
        private bool completed;
        private bool disposed;
        private bool faulted;
        private bool nonOriginPreroll;

        public int RequiredPrerollPackets => configuration.SpectralBandReplication == true || sbrFormat is not null ? 2 : 1;

        public void ResetForPreroll(long packetCount, bool startsAtStreamBeginning)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(packetCount);
            Reset();
            prerollPackets = packetCount;
            nonOriginPreroll = !startsAtStreamBeginning;
        }

        public AacDecoder(int streamIndex, AacConfiguration configuration, MediaBufferAllocator allocator, int maxAccessUnitBytes, bool adts = false)
        {
            this.streamIndex = streamIndex;
            this.configuration = configuration;
            this.allocator = allocator;
            this.maxAccessUnitBytes = maxAccessUnitBytes;
            this.adts = adts;
            pending = new AudioFrame?[adts ? 4 : 1];
            layout = new AacChannelLayout(configuration.ChannelConfiguration);
            coreFormat = layout.CreateFormat(configuration.SampleRate);
            format = coreFormat;
        }

        public CodecInputStatus TrySend(MediaPacket input, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            EnsureUsable();
            bool succeeded = false;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (completed)
                {
                    throw new InvalidOperationException("AAC input is complete.");
                }
                if (outputCount != 0)
                {
                    succeeded = true;
                    return CodecInputStatus.OutputRequired;
                }
                if (input.StreamIndex != streamIndex || input.Data.IsEmpty)
                {
                    throw new InvalidDataException("The AAC packet is empty or belongs to a different stream.");
                }
                ReadPacket(input, cancellationToken);
                succeeded = true;
                return CodecInputStatus.Accepted;
            }
            finally
            {
                if (!succeeded)
                {
                    Fault();
                }
            }
        }

        public CodecOutput<AudioFrame> Receive(CancellationToken cancellationToken = default)
        {
            EnsureUsable();
            if (cancellationToken.IsCancellationRequested)
            {
                Fault();
                cancellationToken.ThrowIfCancellationRequested();
            }
            if (outputCount != 0)
            {
                AudioFrame frame = pending[outputIndex] ?? throw new InvalidOperationException("The AAC output queue is inconsistent.");
                pending[outputIndex++] = null;
                if (outputIndex == outputCount)
                {
                    outputCount = 0;
                    outputIndex = 0;
                }
                return CodecOutput<AudioFrame>.Available(frame);
            }
            return completed ? CodecOutput<AudioFrame>.EndOfStream : CodecOutput<AudioFrame>.InputRequired;
        }

        public void CompleteInput()
        {
            EnsureUsable();
            if (prerollPackets != 0)
            {
                Fault();
                throw new InvalidDataException("AAC input ended before its requested preroll was complete.");
            }
            completed = true;
            workspace?.Dispose();
            workspace = null;
        }

        public void Reset()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            for (int index = 0; index < pending.Length; index++)
            {
                pending[index]?.Dispose();
                pending[index] = null;
            }
            outputCount = 0;
            outputIndex = 0;
            workspace?.Dispose();
            workspace = null;
            noiseState = InitialNoiseState;
            prerollPackets = 0;
            prerollAccessUnits = 0;
            completed = false;
            faulted = false;
            nonOriginPreroll = false;
            format = coreFormat;
        }

        private void Fault()
        {
            Reset();
            faulted = true;
        }

        private void EnsureUsable()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (faulted)
            {
                throw new InvalidOperationException("The AAC decoder has failed and requires reset or disposal.");
            }
        }

        public void Dispose()
        {
            if (!disposed)
            {
                Reset();
                disposed = true;
            }
            GC.SuppressFinalize(this);
        }
    }
}
