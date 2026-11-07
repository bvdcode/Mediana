// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Execution;

namespace Mediana
{
    public partial class ContainerSession
    {
        /// <summary>Plans whole-video encoding intervals from indexed clocks without reading media payload.
        /// Borrows the workspace allocator; the returned immutable intervals survive session disposal.</summary>
        public VideoSegmentPlan PlanVideoSegments(int streamIndex, MediaDuration targetDuration,
            MediaBufferAllocator workspace, CancellationToken cancellationToken = default)
        {
            Enter();
            try
            {
                RequireActive();
                if (Reader is not IIndexedPacketTimingReader timing)
                {
                    throw new NotSupportedException("The container reader does not expose indexed packet clocks.");
                }
                return VideoSegmentPlanner.Create(timing, streamIndex, targetDuration, workspace, cancellationToken);
            }
            finally { Volatile.Write(ref busy, 0); }
        }
    }
}
