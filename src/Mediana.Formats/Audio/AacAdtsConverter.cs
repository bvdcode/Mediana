// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;

namespace Mediana.Formats.Audio
{
    /// <summary>Adds an ADTS header to a raw AAC access unit using parsed AudioSpecificConfig parameters.</summary>
    public class AacAdtsConverter
    {
        private readonly int frequency;
        private readonly int channels;

        public AacAdtsConverter(ReadOnlySpan<byte> configuration)
        {
            AacConfiguration description = AacConfiguration.ReadAudioSpecificConfig(configuration, out string? reason)
                ?? throw new NotSupportedException(reason);
            frequency = description.FrequencyIndex;
            channels = description.ChannelConfiguration;
        }

        /// <summary>Returns an owned ADTS frame. Configuration and payload storage are not retained.</summary>
        public MediaBuffer Convert(ReadOnlySpan<byte> data, MediaBufferAllocator allocator)
        {
            ArgumentNullException.ThrowIfNull(allocator);
            if (data.Length is < 1 or > 8184)
            {
                throw new InvalidDataException("The AAC access unit does not fit a single ADTS frame.");
            }
            int size = data.Length + 7;
            using WritableMediaBuffer result = allocator.Allocate(size);
            Span<byte> output = result.Memory.Span;
            output[0] = 0xFF;
            output[1] = 0xF1;
            output[2] = (byte)(0x40 | frequency << 2 | channels >> 2);
            output[3] = (byte)((channels & 3) << 6 | size >> 11);
            output[4] = (byte)(size >> 3);
            output[5] = (byte)((size & 7) << 5 | 0x1F);
            output[6] = 0xFC;
            data.CopyTo(output[7..]);
            return result.Publish();
        }
    }
}
