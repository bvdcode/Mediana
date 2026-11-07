// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers;
using Mediana.Core.Timing;

namespace Mediana.Tests
{
    internal class PresentationUnitTestReader(ContainerPacketTiming[] packets, ContainerVideoPresentationUnit[] units)
        : VideoTimingTestReader(packets, new(1, 1000)), IIndexedVideoPresentationReader
    {
        public long GetPresentationUnitCount(int streamIndex)
        {
            _ = GetPacketCount(streamIndex);
            return units.Length;
        }

        public ContainerVideoPresentationUnit GetPresentationUnit(int streamIndex, long presentationUnitIndex,
            CancellationToken cancellationToken = default)
        {
            _ = GetPresentationUnitCount(streamIndex);
            cancellationToken.ThrowIfCancellationRequested();
            return units[checked((int)presentationUnitIndex)];
        }
    }
}
