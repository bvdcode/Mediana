// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Core;
using Mediana.Core.Metadata;

namespace Mediana.Tests
{
    internal class FixturePacketMetadataReader(string codecId, int probeLength, StreamMetadata? result)
        : IPacketPrefixMetadataReader
    {
        public string ImplementationId => "fixture.packet-metadata";
        public string CodecId => codecId;
        public int PacketProbeLength => probeLength;
        public StreamMetadata? ConfigurationMetadata { get; init; }
        public Action? OnConfiguration { get; init; }
        public byte[]? Prefix { get; private set; }
        public int PrefixReads { get; private set; }

        public StreamMetadata? Read(EncodedStreamInfo stream)
        {
            OnConfiguration?.Invoke();
            return ConfigurationMetadata;
        }

        public StreamMetadata? ReadPacketPrefix(EncodedStreamInfo stream, ReadOnlySpan<byte> prefix)
        {
            PrefixReads++;
            Prefix = prefix.ToArray();
            return result;
        }
    }
}
