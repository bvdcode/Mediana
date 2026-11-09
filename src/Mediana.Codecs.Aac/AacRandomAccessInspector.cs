// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Formats.Audio;

namespace Mediana.Codecs.Aac
{
    internal class AacRandomAccessInspector(int streamIndex, AacConfiguration configuration, int maxAccessUnitBytes, bool adts = false)
        : IRandomAccessRecoveryInspector, IRandomAccessOriginInspector
    {
        private readonly AacChannelLayout layout = new(configuration.ChannelConfiguration);
        private bool parametricStereo = configuration.ParametricStereo == true;
        private bool originInspected;

        public bool RequiresOriginPacket => !originInspected && layout.ChannelCount == 1
            && configuration.ParametricStereo != false && configuration.SpectralBandReplication != false;

        public RandomAccessRecovery? InspectOrigin(MediaPacket origin, CancellationToken cancellationToken = default)
        {
            AacAccessUnitInspection inspection = Inspect(origin, cancellationToken);
            originInspected = true;
            return inspection.OriginIndependent ? new RandomAccessRecovery(inspection.HasSbr ? 2 : 1) : null;
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
            if (!adts && packet.Data.Length > maxAccessUnitBytes)
            {
                throw new NotSupportedException("The AAC packet exceeds the configured access unit limit.");
            }
            AacAccessUnitInspection inspection;
            bool hasParametricStereo;
            if (adts)
            {
                inspection = AacAdtsPacketParser.Inspect(packet.Data.Span, configuration, layout, maxAccessUnitBytes,
                    cancellationToken, out hasParametricStereo);
            }
            else
            {
                inspection = AacAccessUnitParser.Inspect(packet.Data.Span, configuration.FrequencyIndex,
                    configuration.SampleRate * 2, layout, cancellationToken, AacPacketProtection.GetPrefix(packet));
                inspection.RequireConfiguration(configuration, layout.ChannelCount);
                hasParametricStereo = inspection.HasParametricStereo;
            }
            if (hasParametricStereo)
            {
                parametricStereo = true;
            }
            return inspection;
        }
    }
}
