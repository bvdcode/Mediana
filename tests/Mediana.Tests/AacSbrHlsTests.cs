// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    public class AacSbrHlsTests
    {
        [Fact]
        public async Task ColdOutOfOrderSbrSegmentsUseLocalRestartAndKeepEncodedAudioReproducible()
        {
            await AacSbrHlsTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "AacSbr"));
        }

        [Fact]
        public async Task ColdOutOfOrderStereoSegmentsCanStartBeforeTheFirstIndependentStereoEnvelope()
        {
            await AacSbrHlsTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "AacPs"), parametricStereo: true);
        }
    }
}
