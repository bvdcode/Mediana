// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Formats.Audio;

namespace Mediana.Codecs.Aac
{
    internal readonly record struct AacAccessUnitInspection(bool Independent, bool HasSbr, bool HasParametricStereo,
        bool OriginIndependent, int ByteLength)
    {
        public void RequireConfiguration(AacConfiguration configuration, int channelCount)
        {
            if (HasSbr && configuration.SpectralBandReplication == false)
            {
                throw new InvalidDataException("The SBR payload contradicts the stream configuration.");
            }
            if (!HasSbr && configuration.SpectralBandReplication == true)
            {
                throw new InvalidDataException("The HE-AAC frame is missing its SBR payload.");
            }
            if (HasSbr && channelCount > 2)
            {
                throw new NotSupportedException("SBR decoding requires mono or stereo audio.");
            }
            if (HasParametricStereo && configuration.ParametricStereo == false)
            {
                throw new InvalidDataException("The PS payload contradicts the stream configuration.");
            }
        }
    }
}
