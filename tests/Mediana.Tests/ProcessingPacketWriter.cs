// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers;
using Mediana.Core;

namespace Mediana.Tests
{
    internal class ProcessingPacketWriter : IContainerWriter
    {
        public List<ProcessingPacket> Packets { get; } = [];
        public bool Complete { get; private set; }
        public bool Disposed { get; private set; }
        public Action? OnWrite { get; init; }

        public ValueTask WritePacketAsync(MediaPacket packet, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Packets.Add(new(packet.StreamIndex, packet.PresentationTime, packet.DecodeTime, packet.Duration, packet.Data.ToArray()));
            OnWrite?.Invoke();
            return ValueTask.CompletedTask;
        }

        public ValueTask CompleteAsync(CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); Complete = true; return ValueTask.CompletedTask; }

        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}
