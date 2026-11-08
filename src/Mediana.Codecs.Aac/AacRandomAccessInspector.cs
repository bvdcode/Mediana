// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Formats.Audio;

namespace Mediana.Codecs.Aac
{
    internal class AacRandomAccessInspector(int streamIndex, AacConfiguration configuration, int maxAccessUnitBytes)
        : IRandomAccessRecoveryInspector
    {
        private readonly AacChannelLayout layout = new(configuration.ChannelConfiguration);

        public bool IsRandomAccess(MediaPacket packet, CancellationToken cancellationToken = default) =>
            InspectRecovery(packet, cancellationToken).HasValue;

        public RandomAccessRecovery? InspectRecovery(MediaPacket packet, CancellationToken cancellationToken = default)
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
                configuration.ExtensionSampleRate ?? configuration.SampleRate * 2, layout, cancellationToken);
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
                throw new NotSupportedException("SBR decoding requires dual-rate mono or stereo audio.");
            }
            return inspection.Independent ? new RandomAccessRecovery(sbr ? 2 : 1) : null;
        }
    }
}
