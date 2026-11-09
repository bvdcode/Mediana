// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Formats.Audio;

namespace Mediana.Codecs.Aac
{
    internal class AacPacketIntegrityValidator(int streamIndex, AacConfiguration configuration, int maxAccessUnitBytes, bool adts = false)
        : IPacketIntegrityValidator
    {
        private readonly AacChannelLayout layout = new(configuration.ChannelConfiguration);
        public string SideDataKind => AdtsPacketProtection.SideDataKind;

        public PacketIntegrityStatus Validate(MediaPacket packet, CancellationToken cancellationToken = default)
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
            if (adts)
            {
                AdtsFrameLayout frame = new(packet.Data.Span);
                frame.RequireConfiguration(configuration);
                if (!frame.Header.HasCrc)
                {
                    return PacketIntegrityStatus.NotPresent;
                }
                AacAdtsPacketParser.Inspect(packet.Data.Span, configuration, layout, maxAccessUnitBytes, cancellationToken, out _);
                return PacketIntegrityStatus.Verified;
            }
            ReadOnlySpan<byte> protection = AacPacketProtection.GetPrefix(packet);
            if (protection.IsEmpty)
            {
                return PacketIntegrityStatus.NotPresent;
            }
            AacAccessUnitParser.Inspect(packet.Data.Span, configuration.FrequencyIndex, configuration.SampleRate * 2,
                layout, cancellationToken, protection);
            return PacketIntegrityStatus.Verified;
        }
    }
}
