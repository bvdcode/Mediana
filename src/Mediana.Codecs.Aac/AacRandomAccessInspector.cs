// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Formats.Audio;

namespace Mediana.Codecs.Aac
{
    internal class AacRandomAccessInspector(int streamIndex, AacConfiguration configuration, int maxAccessUnitBytes)
        : IRandomAccessRecoveryInspector, IRandomAccessOriginInspector
    {
        private readonly AacChannelLayout layout = new(configuration.ChannelConfiguration);
        private bool parametricStereo = configuration.ParametricStereo == true;
        private bool originInspected;

        public bool RequiresOriginPacket => !originInspected && layout.ChannelCount == 1
            && configuration.ParametricStereo is null && configuration.SpectralBandReplication != false;

        public void InspectOrigin(MediaPacket origin, CancellationToken cancellationToken = default)
        {
            Inspect(origin, cancellationToken);
            originInspected = true;
        }

        public bool IsRandomAccess(MediaPacket packet, CancellationToken cancellationToken = default) =>
            InspectRecovery(packet, cancellationToken).HasValue;

        public RandomAccessRecovery? InspectRecovery(MediaPacket packet, CancellationToken cancellationToken = default)
        {
            AacAccessUnitInspection inspection = Inspect(packet, cancellationToken);
            return inspection.Independent && (!parametricStereo || inspection.HasParametricStereo)
                ? new RandomAccessRecovery(inspection.HasSbr ? 2 : 1) : null;
        }

        private AacAccessUnitInspection Inspect(MediaPacket packet, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(packet);
            cancellationToken.ThrowIfCancellationRequested();
            if (packet.StreamIndex != streamIndex)
            {
                throw new ArgumentException("The packet belongs to a different AAC stream.", nameof(packet));
            }
            if (packet.Data.Length > maxAccessUnitBytes)
            {
                throw new NotSupportedException("The AAC packet exceeds the configured access unit limit.");
            }
            AacAccessUnitInspection inspection = AacAccessUnitParser.Inspect(packet.Data.Span, configuration.FrequencyIndex,
                configuration.SampleRate * 2, layout, cancellationToken, AacPacketProtection.GetPrefix(packet));
            bool sbr = inspection.HasSbr;
            if (sbr && configuration.SpectralBandReplication == false)
            {
                throw new InvalidDataException("The SBR payload contradicts the stream configuration.");
            }
            if (!sbr && configuration.SpectralBandReplication == true)
            {
                throw new InvalidDataException("The HE-AAC frame is missing its SBR payload.");
            }
            if (sbr && layout.ChannelCount > 2)
            {
                throw new NotSupportedException("SBR decoding requires mono or stereo audio.");
            }
            if (inspection.HasParametricStereo)
            {
                if (configuration.ParametricStereo == false)
                {
                    throw new InvalidDataException("The PS payload contradicts the stream configuration.");
                }
                parametricStereo = true;
            }
            return inspection;
        }
    }
}
