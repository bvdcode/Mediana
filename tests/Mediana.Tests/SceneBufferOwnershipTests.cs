// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    public class SceneBufferOwnershipTests
    {
        [Fact]
        public void ReusedBuffersAreClearedAndRetainTheirReservation() => SceneBufferOwnershipTestInput.VerifyReuse();

        [Fact]
        public void TrimmingDisposesOnlyIdleBuffersAndReleasesTheirReservation() => SceneBufferOwnershipTestInput.VerifyTrim();

        [Fact]
        public void DisposalReleasesActiveAndIdleBuffers() => SceneBufferOwnershipTestInput.VerifyDisposal();

        [Fact]
        public void FailedSurfaceConstructionReleasesItsReservation() => SceneBufferOwnershipTestInput.VerifyFailedConstruction();

        [Fact]
        public void ReservationDisposalIsIdempotent() => SceneBufferOwnershipTestInput.VerifyReservationDisposal();
    }
}
