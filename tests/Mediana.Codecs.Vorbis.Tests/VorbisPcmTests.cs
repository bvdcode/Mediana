// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Tests;

namespace Mediana.Codecs.Vorbis.Tests
{
    public class VorbisPcmTests
    {
        [Fact]
        public void SequentialPcmAndBlockTransitionsMatchIndependentReference() =>
            Assert.Equal(1183, VorbisPcmTestInput.Verify(Path.Combine(AppContext.BaseDirectory, "Data")));

        [Fact]
        public void SequentialFrameDecodingAllocatesNoWorkingStorage() =>
            Assert.Equal(1183, VorbisPcmTestInput.Verify(Path.Combine(AppContext.BaseDirectory, "Data"), true));
    }
}
