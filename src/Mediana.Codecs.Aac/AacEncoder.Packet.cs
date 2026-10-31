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
            for (int channel = 0; channel < channels; channel++)
            {
                AacAnalysis.Transform(current.Input(channel), 0, 0, 0, current.Spectrum(channel), current.Window,
                    current.Folded, current.Fourier, token);
            }
            Span<int> gains = stackalloc int[channels];
            AacSpectrumQuantizer.Quantize(current.Spectra, current.Window, current.Quantized, current.Books, current.Scales,
                gains, configuration.FrequencyIndex, frameBits - 64, token);
            for (int channel = 0; channel < channels; channel++)
            {
                AacChannelEncoder.Write(ref bits, gains[channel], current.Quantized.Slice(channel * FrameSamples, FrameSamples),
                    current.Books.Slice(channel * AacSpectrumQuantizer.ChannelBands, AacSpectrumQuantizer.ChannelBands),
                    current.Scales.Slice(channel * AacSpectrumQuantizer.ChannelBands, AacSpectrumQuantizer.ChannelBands),
                    configuration.FrequencyIndex, channels == 2);
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
