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
            long budget = budgetRemainder + (long)options.BitRate * FrameSamples;
            int frameBits = (int)(budget / format.SampleRate) & ~7;
            budgetRemainder = budget % format.SampleRate;
            Span<int> offsets = stackalloc int[AacSpectrumQuantizer.ChannelBands + 1];
            AacEncodingWindow window = new(configuration.FrequencyIndex, 0, 0, [1], offsets);
            Span<byte> encoded = stackalloc byte[1536];
            int length = AacPacketEncoder.Encode(current, window, 0, frameBits, encoded, token);
            token.ThrowIfCancellationRequested();
            using WritableMediaBuffer storage = allocator.Allocate(length);
            encoded[..length].CopyTo(storage.Memory.Span);
            using MediaBuffer buffer = storage.Publish();
            MediaTimestamp timestamp = new(checked(firstTimestamp!.Value + (packets - 1) * FrameSamples * sampleTicks), Stream.TimeBase);
            return new(Stream.StreamIndex, buffer, timestamp, timestamp, new(FrameSamples * sampleTicks, Stream.TimeBase));
        }
    }
}
