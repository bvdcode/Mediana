// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Core;

namespace Mediana.Tests
{
    internal class OriginInspectorProbe(Func<MediaPacket, RandomAccessRecovery?> inspect, bool requiresOrigin = true,
        Action<MediaPacket>? inspectOrigin = null) : RecoveryInspectorProbe(inspect), IRandomAccessOriginInspector
    {
        public bool RequiresOriginPacket => requiresOrigin;
        public int OriginReads { get; private set; }
        public MediaPacket? Origin { get; private set; }

        public void InspectOrigin(MediaPacket origin, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OriginReads++;
            Origin = origin;
            inspectOrigin?.Invoke(origin);
        }
    }
}
