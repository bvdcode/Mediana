// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;

namespace Mediana.Codecs.Aac
{
    internal partial class AacDecoder
    {
        private int DecodeAccessUnit(ReadOnlySpan<byte> data, MediaTimestamp? timestamp, bool discard,
            CancellationToken cancellationToken, ReadOnlySpan<byte> protection = default,
            bool allowTrailingBlocks = false, ushort? rawDataBlockCrc = null)
        {
            workspace ??= new AacDecoderWorkspace(allocator, layout);
            int sbrRate = configuration.SampleRate * 2;
            int byteLength = AacAccessUnitParser.Read(data, configuration.FrequencyIndex, sbrRate, workspace, ref noiseState,
                cancellationToken, protection, allowTrailingBlocks, rawDataBlockCrc);
            if (byteLength > maxAccessUnitBytes)
            {
                throw new NotSupportedException("The AAC raw data block exceeds the configured access unit limit.");
            }
            bool sbr = workspace.SbrElements.Any(element => element is not null);
            if (sbr && configuration.SpectralBandReplication == false)
            {
                throw new InvalidDataException("The SBR payload contradicts the stream configuration.");
            }
            if (!sbr && configuration.SpectralBandReplication == true)
            {
                throw new InvalidDataException("The HE-AAC frame is missing its SBR payload.");
            }
            if (sbr && nonOriginPreroll && !discard && prerollAccessUnits < 2)
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
                bool stereoReconstruction = configuration.ParametricStereo == true || workspace.SbrElements[0]?.ParametricStereo is not null;
                if (stereoReconstruction && configuration.ParametricStereo == false)
                {
                    throw new InvalidDataException("The PS payload contradicts the stream configuration.");
                }
                if (stereoReconstruction && layout.ChannelCount != 1)
                {
                    throw new InvalidDataException("PS reconstruction requires a monaural AAC core.");
                }
                int outputRate = configuration.ExtensionSampleRate ?? sbrRate;
                if (sbrFormat is null || stereoReconstruction && sbrFormat.Channels.Count != 2)
                {
                    sbrFormat = stereoReconstruction ? new AacChannelLayout(2).CreateFormat(outputRate) : layout.CreateFormat(outputRate);
                }
                format = sbrFormat;
            }
            int sampleCount = sbr ? 1024 * format.SampleRate / configuration.SampleRate : 1024;
            if (discard)
            {
                for (int channel = 0; channel < workspace.Channels.Length; channel++)
                {
                    Synthesize(channel, [], cancellationToken);
                }
                cancellationToken.ThrowIfCancellationRequested();
                prerollAccessUnits++;
                return byteLength;
            }
            using WritableMediaBuffer writable = allocator.Allocate(format.GetBufferSize(sampleCount));
            Span<float> output = MemoryMarshal.Cast<byte, float>(writable.Memory.Span);
            for (int channel = 0; channel < workspace.Channels.Length; channel++)
            {
                Synthesize(channel, output, cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            using MediaBuffer buffer = writable.Publish();
            pending[outputCount++] = new AudioFrame(format, sampleCount, buffer, timestamp);
            return byteLength;
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
                state.Process(synthesized, output, channelIndex, format.Channels.Count, element, element.Channels[codedChannel], element.Patches,
                    layout.ChannelCount == 1 && format.Channels.Count == 2, cancellationToken);
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
    }
}
