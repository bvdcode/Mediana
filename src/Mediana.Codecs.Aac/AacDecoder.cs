// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;

using Mediana.Formats.Audio;

namespace Mediana.Codecs.Aac
{
    internal class AacDecoder : IMediaCodec<MediaPacket, AudioFrame>, IPacketPrerollDecoder
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
        private AudioFrame? pending;
        private long prerollPackets;
        private long initialPrerollPackets;
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
            initialPrerollPackets = packetCount;
            nonOriginPreroll = !startsAtStreamBeginning;
        }

        public AacDecoder(int streamIndex, AacConfiguration configuration, MediaBufferAllocator allocator, int maxAccessUnitBytes)
        {
            this.streamIndex = streamIndex;
            this.configuration = configuration;
            this.allocator = allocator;
            this.maxAccessUnitBytes = maxAccessUnitBytes;
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
                if (pending is not null)
                {
                    succeeded = true;
                    return CodecInputStatus.OutputRequired;
                }
                if (input.StreamIndex != streamIndex || input.Data.IsEmpty)
                {
                    throw new InvalidDataException("The AAC packet is empty or belongs to a different stream.");
                }
                if (input.Data.Length > maxAccessUnitBytes)
                {
                    throw new NotSupportedException("The AAC packet exceeds the configured access unit limit.");
                }
                workspace ??= new AacDecoderWorkspace(allocator, layout);
                int sbrRate = configuration.SampleRate * 2;
                AacAccessUnitParser.Read(input.Data.Span, configuration.FrequencyIndex, sbrRate, workspace, ref noiseState, cancellationToken);
                bool sbr = workspace.SbrElements.Any(element => element is not null);
                if (sbr && configuration.SpectralBandReplication == false)
                {
                    throw new InvalidDataException("The SBR payload contradicts the stream configuration.");
                }
                if (!sbr && configuration.SpectralBandReplication == true)
                {
                    throw new InvalidDataException("The HE-AAC frame is missing its SBR payload.");
                }
                if (sbr && nonOriginPreroll && initialPrerollPackets < 2)
                {
                    throw new NotSupportedException("SBR seeking requires a restart header and at least two preroll packets.");
                }
                if (sbr && layout.ChannelCount > 2)
                {
                    throw new NotSupportedException("SBR decoding requires mono or stereo audio.");
                }
                if (sbr)
                {
                    foreach (AacSbrElement? element in workspace.SbrElements)
                    {
                        if (element is null || !element.Present)
                        {
                            throw new InvalidDataException("The AAC frame is missing its SBR channel data.");
                        }
                    }
                    format = sbrFormat ??= layout.CreateFormat(configuration.ExtensionSampleRate ?? sbrRate);
                }
                int sampleCount = sbr ? 1024 * format.SampleRate / configuration.SampleRate : 1024;
                if (prerollPackets != 0)
                {
                    for (int channel = 0; channel < workspace.Channels.Length; channel++)
                    {
                        Synthesize(channel, [], cancellationToken);
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    prerollPackets--;
                    succeeded = true;
                    return CodecInputStatus.Accepted;
                }
                using WritableMediaBuffer writable = allocator.Allocate(format.GetBufferSize(sampleCount));
                Span<float> output = MemoryMarshal.Cast<byte, float>(writable.Memory.Span);
                for (int channel = 0; channel < workspace.Channels.Length; channel++)
                {
                    Synthesize(channel, output, cancellationToken);
                }
                cancellationToken.ThrowIfCancellationRequested();
                using MediaBuffer buffer = writable.Publish();
                pending = new AudioFrame(format, sampleCount, buffer, input.PresentationTime);
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

        private void Synthesize(int codedChannel, Span<float> output, CancellationToken cancellationToken)
        {
            AacDecoderWorkspace current = workspace ?? throw new InvalidOperationException("AAC workspace is missing.");
            AacChannelState channel = current.Channels[codedChannel];
            int channelIndex = layout.OutputChannel(codedChannel);
            Span<double> synthesized = current.Output;
            AacSynthesis.Process(channel.Spectrum, channel.Info.Sequence, channel.Info.Shape, channel.PreviousShape,
                channel.Overlap, synthesized, current.Time, current.Fourier, cancellationToken);
            channel.PreviousShape = channel.Info.Shape;
            if (current.SbrElements.Length != 0)
            {
                AacSbrElement element = current.SbrElements[0] ?? throw new InvalidOperationException("The SBR element is missing.");
                AacSbrChannelState state = current.SbrChannels[codedChannel] ??=
                    new AacSbrChannelState(allocator, 32 * format.SampleRate / configuration.SampleRate);
                state.Process(synthesized, output, channelIndex, format.Channels.Count, element, element.Channels[codedChannel], element.Patches, cancellationToken);
                return;
            }
            if (output.IsEmpty)
            {
                foreach (double sample in synthesized)
                {
                    if (!float.IsFinite((float)sample))
                    {
                        throw new InvalidDataException("AAC synthesis produced a nonfinite PCM sample.");
                    }
                }
                return;
            }
            int channels = format.Channels.Count;
            for (int index = 0; index < 1024; index++)
            {
                float value = (float)synthesized[index];
                if (!float.IsFinite(value))
                {
                    throw new InvalidDataException("AAC synthesis produced a nonfinite PCM sample.");
                }
                output[index * channels + channelIndex] = value;
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
            if (pending is not null)
            {
                AudioFrame frame = pending;
                pending = null;
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
            pending?.Dispose();
            pending = null;
            workspace?.Dispose();
            workspace = null;
            noiseState = InitialNoiseState;
            prerollPackets = 0;
            initialPrerollPackets = 0;
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
