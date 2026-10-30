// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers;
using Mediana.Core;

namespace Mediana.Tests
{
    internal class CollectingPacketWriter : IContainerWriter
    {
        public List<byte[]> Packets { get; } = [];
        public List<int> Streams { get; } = [];
        public bool IsComplete { get; private set; }
        public bool IsDisposed { get; private set; }
        public bool FailWrites { get; init; }
        public ValueTask WritePacketAsync(MediaPacket packet, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (FailWrites)
            {
                throw new IOException("Output failure.");
            }
            Packets.Add(packet.Data.ToArray());
            Streams.Add(packet.StreamIndex);
            return ValueTask.CompletedTask;
        }
        public ValueTask CompleteAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IsComplete = true;
            return ValueTask.CompletedTask;
        }
        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
