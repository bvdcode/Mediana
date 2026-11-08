// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Metadata;

namespace Mediana.Containers.Ac3
{
    public partial class Ac3Reader : IContainerStreamMetadataProvider
    {
        private readonly AudioStreamMetadata openingMetadata;

        public StreamMetadata? GetStreamMetadata(int streamIndex)
        {
            Enter();
            try { RequireStream(streamIndex); return openingMetadata; }
            finally { Exit(); }
        }
    }
}
