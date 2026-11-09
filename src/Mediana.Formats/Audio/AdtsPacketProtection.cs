// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Formats.Audio
{
    /// <summary>Identifies a protected single-block ADTS packet's original seven-byte header and two-byte CRC.
    /// The CRC also covers syntax-defined regions of the stripped AAC payload.</summary>
    public static class AdtsPacketProtection
    {
        public const string SideDataKind = "adts.protection";
        public const int PrefixByteLength = AdtsFrameHeader.ByteLength + 2;
    }
}
