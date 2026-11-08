// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Formats.Audio;

namespace Mediana.Codecs.Aac
{
    internal class AacSbrDecoder(int streamIndex, AacConfiguration configuration, MediaBufferAllocator allocator, int maxAccessUnitBytes,
        bool adts = false)
        : IMediaCodec<MediaPacket, AudioFrame>, IPacketPrerollDecoder
    {
        private readonly AacDecoder decoder = new(streamIndex, configuration, allocator, maxAccessUnitBytes, adts);

        public int RequiredPrerollPackets => 2;
        public void ResetForPreroll(long packetCount, bool startsAtStreamBeginning) =>
            decoder.ResetForPreroll(packetCount, startsAtStreamBeginning);

        public CodecInputStatus TrySend(MediaPacket input, CancellationToken cancellationToken = default) => decoder.TrySend(input, cancellationToken);
        public CodecOutput<AudioFrame> Receive(CancellationToken cancellationToken = default) => decoder.Receive(cancellationToken);
        public void CompleteInput() => decoder.CompleteInput();
        public void Reset() => decoder.Reset();
        public void Dispose() => decoder.Dispose();
    }
}
