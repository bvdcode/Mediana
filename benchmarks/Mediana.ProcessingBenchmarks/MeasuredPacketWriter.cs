// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Diagnostics;
using Mediana.Containers;
using Mediana.Core;

namespace Mediana.ProcessingBenchmarks
{
    internal class MeasuredPacketWriter(IContainerWriter writer, Stopwatch watch) : IContainerWriter
    {
        public double? FirstPacketMilliseconds { get; private set; }
        public ValueTask WritePacketAsync(MediaPacket packet, CancellationToken cancellationToken = default)
        {
            FirstPacketMilliseconds ??= watch.Elapsed.TotalMilliseconds;
            return writer.WritePacketAsync(packet, cancellationToken);
        }
        public ValueTask CompleteAsync(CancellationToken cancellationToken = default) => writer.CompleteAsync(cancellationToken);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
