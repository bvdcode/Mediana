// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Formats.Audio;

namespace Mediana.Codecs.Aac
{
    internal static class AacPacketProtection
    {
        public static ReadOnlySpan<byte> GetPrefix(MediaPacket packet)
        {
            if (!packet.TryGetSideData(AdtsPacketProtection.SideDataKind, out MediaPacketSideDataView protection))
            {
                return default;
            }
            if (protection.Data.Length != AdtsPacketProtection.PrefixByteLength)
            {
                throw new InvalidDataException("The ADTS protection prefix must contain one header and CRC word.");
            }
            return protection.Data.Span;
        }
    }
}
