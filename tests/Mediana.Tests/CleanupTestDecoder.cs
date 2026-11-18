// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public class CleanupTestDecoder(bool throws, WritableMediaBuffer memory) : IMediaCodec<MediaPacket, AudioFrame>
    {
        public bool Disposed { get; private set; }
        public CodecInputStatus TrySend(MediaPacket input, CancellationToken cancellationToken = default) => CodecInputStatus.Accepted;
        public CodecOutput<AudioFrame> Receive(CancellationToken cancellationToken = default) => CodecOutput<AudioFrame>.InputRequired;
        public void CompleteInput() { }
        public void Reset() { }
        public void Dispose() { Disposed = true; memory.Dispose(); if (throws) { throw new IOException("Decoder disposal failed"); } }
    }
}
