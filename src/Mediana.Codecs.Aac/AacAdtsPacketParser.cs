// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Formats.Audio;

namespace Mediana.Codecs.Aac
{
    internal static class AacAdtsPacketParser
    {
        public static AacAccessUnitInspection Inspect(ReadOnlySpan<byte> data, AacConfiguration configuration,
            AacChannelLayout layout, int maximumBytes, CancellationToken cancellationToken, out bool hasParametricStereo)
        {
            AacAdtsBlockReader blocks = new(data, configuration, maximumBytes);
            AacAccessUnitInspection first = default;
            hasParametricStereo = false;
            while (blocks.Index < blocks.BlockCount)
            {
                ReadOnlySpan<byte> payload = blocks.Read(out ReadOnlySpan<byte> protection, out ushort? crc, out bool trailing);
                AacAccessUnitInspection current = AacAccessUnitParser.Inspect(payload, configuration.FrequencyIndex,
                    configuration.SampleRate * 2, layout, cancellationToken, protection, trailing, crc);
                current.RequireConfiguration(configuration, layout.ChannelCount);
                if (blocks.Index == 0)
                {
                    first = current;
                }
                hasParametricStereo |= current.HasParametricStereo;
                blocks.Advance(current.ByteLength);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return first;
        }
    }
}
