// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Formats.Audio;

namespace Mediana.Containers.Adts
{
    internal static class AdtsFrameValidation
    {
        public const int SamplesPerFrame = 1024;

        public static void RequireSingleBlock(AdtsFrameHeader header)
        {
            if (header.RawDataBlockCount != 1)
            {
                throw new NotSupportedException("Multiple raw data blocks in one ADTS frame require explicit block separation.");
            }
        }

        public static void RequireConfiguration(AdtsFrameHeader first, AdtsFrameHeader next)
        {
            RequireSingleBlock(next);
            if (next.IsMpeg2 != first.IsMpeg2 || next.AudioObjectType != first.AudioObjectType
                || next.FrequencyIndex != first.FrequencyIndex || next.ChannelConfiguration != first.ChannelConfiguration)
            {
                throw new NotSupportedException("The ADTS stream changes its MPEG version, audio object type, sample rate or channel configuration.");
            }
        }
    }
}
