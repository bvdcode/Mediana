// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Core;
using Mediana.Core.Metadata;

namespace Mediana.Tests
{
    internal class FixtureStreamMetadataReader(string id, string codecId,
        Func<EncodedStreamInfo, StreamMetadata?> read) : IStreamMetadataReader
    {
        public string ImplementationId => id;
        public string CodecId => codecId;
        public StreamMetadata? Read(EncodedStreamInfo stream) => read(stream);
    }
}
