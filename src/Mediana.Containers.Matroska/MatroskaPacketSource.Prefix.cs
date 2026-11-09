// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.IO;

namespace Mediana.Containers.Matroska
{
    internal partial class MatroskaPacketSource
    {
        public async ValueTask<int> ReadPrefixAtAsync(int streamIndex, long packetIndex, Memory<byte> destination,
            CancellationToken token)
        {
            Enter();
            try
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                token.ThrowIfCancellationRequested();
                MatroskaPacketAddress address = index.Locate(streamIndex, packetIndex);
                MatroskaIndexedBlock block = index.GetBlock(address.BlockIndex);
                if (destination.IsEmpty) { return 0; }
                MatroskaFrameRange frame = await ReadFrameRangeAsync(address, block, token).ConfigureAwait(false);
                int count = Math.Min(destination.Length, frame.Length);
                await source.ReadExactlyAtAsync(frame.Offset, destination[..count], token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                return count;
            }
            finally { Volatile.Write(ref busy, 0); }
        }

        private async ValueTask<MatroskaFrameRange> ReadFrameRangeAsync(MatroskaPacketAddress address,
            MatroskaIndexedBlock block, CancellationToken token)
        {
            if (cachedBlock != address.BlockIndex)
            {
                cachedBlock = -1;
                MatroskaBlockHeader header = await blocks.ReadAsync(block.Element, token).ConfigureAwait(false);
                if (header.TrackNumber != tracks[block.StreamIndex].Number || header.FrameCount != block.FrameCount ||
                    header.RelativeTimestamp != block.RelativeTimestamp || header.Flags != block.Flags)
                {
                    throw new InvalidDataException("The Matroska block no longer matches its index.");
                }
                cachedBlock = address.BlockIndex;
            }
            return blocks.GetFrame(address.FrameIndex);
        }
    }
}
