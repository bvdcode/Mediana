// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;

namespace Mediana.Codecs.Aac
{
    public partial class AacEncoder
    {
        private MediaPacket Encode(CancellationToken token)
        {
            AacEncoderWorkspace current = workspace ?? throw new InvalidOperationException("AAC analysis workspace is missing.");
            int channels = format.Channels.Count;
            long budget = budgetRemainder + (long)options.BitRate * FrameSamples;
            int frameBits = (int)(budget / format.SampleRate) & ~7;
            budgetRemainder = budget % format.SampleRate;
            int channelBudget = (frameBits - 64) / channels;
            Span<byte> encoded = stackalloc byte[1536];
            AacBitWriter bits = new(encoded[..(frameBits / 8)]);
            bits.Write(channels == 1 ? 0u : 1, 3);
            bits.Write(0, 4);
            if (channels == 2)
            {
                bits.Write(1, 1);
                AacChannelEncoder.WriteWindow(ref bits, configuration.FrequencyIndex);
                bits.Write(0, 2);
            }
            ReadOnlySpan<double> window = AacWindows.Get(FrameSamples, 0);
            for (int channel = 0; channel < channels; channel++)
            {
                Span<double> analysis = current.Window;
                ReadOnlySpan<double> samples = current.Input(channel);
                for (int index = 0; index < FrameSamples; index++)
                {
                    analysis[index] = samples[index] * window[index];
                    analysis[FrameSamples + index] = samples[FrameSamples + index] * window[FrameSamples - 1 - index];
                }
                AacMdct.Transform(analysis, current.Spectrum(channel), current.Folded, analysis, current.Fourier, token);
                int gain = AacChannelEncoder.Quantize(current.Spectrum(channel), current.Quantized(channel), current.Books(channel),
                    configuration.FrequencyIndex, channelBudget, token);
                AacChannelEncoder.Write(ref bits, gain, current.Quantized(channel), current.Books(channel), configuration.FrequencyIndex, channels == 2);
            }
            int length = bits.Finish();
            token.ThrowIfCancellationRequested();
            using WritableMediaBuffer storage = allocator.Allocate(length);
            encoded[..length].CopyTo(storage.Memory.Span);
            using MediaBuffer buffer = storage.Publish();
            MediaTimestamp timestamp = new(checked(firstTimestamp!.Value + (packets - 1) * FrameSamples * sampleTicks), Stream.TimeBase);
            return new(Stream.StreamIndex, buffer, timestamp, timestamp, new(FrameSamples * sampleTicks, Stream.TimeBase));
        }
    }
}
