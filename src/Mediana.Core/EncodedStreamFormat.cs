// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Core
{
    /// <summary>Value description of configuration syntax, payload framing and packet boundaries.
    /// Identifiers are ordinal, case-sensitive names defined by the format module.</summary>
    public record EncodedStreamFormat
    {
        public EncodedStreamFormat(string configurationSyntax, string packetFraming, EncodedPacketBoundary boundary)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(configurationSyntax);
            ArgumentException.ThrowIfNullOrWhiteSpace(packetFraming);
            if (!Enum.IsDefined(boundary))
            {
                throw new ArgumentOutOfRangeException(nameof(boundary));
            }
            ConfigurationSyntax = configurationSyntax;
            PacketFraming = packetFraming;
            Boundary = boundary;
        }

        public string ConfigurationSyntax { get; }
        public string PacketFraming { get; }
        public EncodedPacketBoundary Boundary { get; }

        public string? GetUnsupportedReason(EncodedStreamFormat actual)
        {
            ArgumentNullException.ThrowIfNull(actual);
            return this == actual ? null :
                $"Expected {ConfigurationSyntax}/{PacketFraming}/{Boundary}; received {actual.ConfigurationSyntax}/{actual.PacketFraming}/{actual.Boundary}.";
        }

        public void Require(EncodedStreamFormat actual)
        {
            ArgumentNullException.ThrowIfNull(actual);
            string? reason = GetUnsupportedReason(actual);
            if (reason is not null)
            {
                throw new NotSupportedException(reason);
            }
        }
    }
}
