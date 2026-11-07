// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Tests;

namespace Mediana.Codecs.Hevc.Tests
{
    public class HevcMotionCompensationTests
    {
        [Fact]
        public async Task FractionalMotionWeightingAndPictureBordersMatchIndependentMainAndMain10Predictions()
        {
            Assert.Equal(768, await HevcMotionCompensationTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data")));
        }

        [Fact]
        public async Task MotionCompensationReusesItsBoundedWorkingStorage()
        {
            string root = Path.Combine(AppContext.BaseDirectory, "Data");
            await HevcMotionCompensationTestInput.VerifyAsync(root);
            Assert.Equal(768, await HevcMotionCompensationTestInput.VerifyAsync(root, true));
        }

        [Fact]
        public void MotionWorkspaceHonorsTheAllocatorBudget()
        {
            MediaBufferAllocator allocator = new(1);
            Assert.Throws<MediaMemoryLimitException>(() => new HevcMotionCompensator(allocator));
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
