// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Tests;

namespace Mediana.Codecs.Hevc.Tests
{
    public class HevcCabacTests
    {
        [Fact]
        public void AllInitializationTypesDecodeIndependentBinsAndWavefrontSnapshots()
        {
            Assert.True(HevcCabacTestInput.Verify(Path.Combine(AppContext.BaseDirectory, "Data")) > 100000);
        }

        [Fact]
        public void EntropyLoopDoesNotAllocateWorkingStorage()
        {
            string root = Path.Combine(AppContext.BaseDirectory, "Data");
            HevcCabacTestInput.Verify(root);
            Assert.True(HevcCabacTestInput.Verify(root, true) > 100000);
        }

        [Fact]
        public void ContextIndicesCannotCrossIntoAnotherSyntaxGroup()
        {
            HevcCabacReader reader = new(new(new ArraySegment<byte>(new byte[4])), 26);
            Assert.Throws<IndexOutOfRangeException>(() => reader.Read(CabacElement.SplitCodingUnit, 3));
            Assert.Throws<IndexOutOfRangeException>(() => reader.Read(CabacElement.Skip));
            Assert.Throws<ArgumentOutOfRangeException>(() => new HevcCabacReader(new(new ArraySegment<byte>(new byte[4])), 26, 3));
        }
    }
}
