// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.Mp4;

namespace Mediana.Tests
{
    public class Mp4FragmentRecoveryTests
    {
        [Fact]
        public async Task EveryFragmentMapsItsSamplesToTheInitializationRecoveryDescription() =>
            await Mp4FragmentRecoveryTestInput.VerifyAsync();

        [Fact]
        public void RecoveryRequiresAKnownKindAndNonzeroDistance()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new Mp4RecoveryDescription((Mp4RecoveryKind)(-1), -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Mp4RecoveryDescription((Mp4RecoveryKind)int.MaxValue, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Mp4RecoveryDescription(Mp4RecoveryKind.Roll, 0));
        }
    }
}
