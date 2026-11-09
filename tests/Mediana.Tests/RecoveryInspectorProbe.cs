// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Core;
using Mediana.Core.Timing;

namespace Mediana.Tests
{
    internal class RecoveryInspectorProbe(Func<MediaPacket, RandomAccessRecovery?> inspect) : IRandomAccessRecoveryInspector
    {
        public List<MediaTimestamp?> Inspections { get; } = [];

        public bool IsRandomAccess(MediaPacket packet, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Recovery inspection must not parse the same candidate twice.");

        public RandomAccessRecovery? InspectRecovery(MediaPacket packet, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Inspections.Add(packet.PresentationTime);
            return inspect(packet);
        }
    }
}
