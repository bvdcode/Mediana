// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Tests;

namespace Mediana.Codecs.Opus.Tests
{
    public class CeltImdctTests
    {
        [Fact]
        public void MixedRadixTransformsAndOverlapMatchTheIndependentDecoder() =>
            Assert.Equal(128, OpusImdctTestInput.Verify(Path.Combine(AppContext.BaseDirectory, "Data")));

        [Fact]
        public void RepeatedTransformsReuseBudgetedTablesAndCallerStorage()
        {
            MediaBufferAllocator allocator = new(32 * 1024);
            using CeltTransformPlan plan = CeltTransformPlan.Create(allocator);
            Span<float> spectrum = stackalloc float[960], output = stackalloc float[1020], workspace = stackalloc float[960];
            spectrum.Fill(.125f); output.Clear();
            CeltImdct.Transform(plan, spectrum, output, workspace, 3, 1);
            long before = GC.GetAllocatedBytesForCurrentThread();
            long budget = allocator.AllocatedBytes;
            for (int at = 0; at < 1000; at++)
            {
                output[960..].CopyTo(output);
                CeltImdct.Transform(plan, spectrum, output, workspace, 3, 1);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(0, allocated);
            Assert.Equal(budget, allocator.AllocatedBytes);
            foreach (float coefficient in spectrum) { Assert.Equal(.125f, coefficient); }
            plan.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void CancellationLeavesBothOutputAndWorkspaceUnchanged()
        {
            MediaBufferAllocator allocator = new(32 * 1024);
            using CeltTransformPlan plan = CeltTransformPlan.Create(allocator);
            float[] input = new float[120], output = Enumerable.Repeat(17f, 180).ToArray();
            float[] workspace = Enumerable.Repeat(19f, 120).ToArray();
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();
            Assert.Throws<OperationCanceledException>(() => CeltImdct.Transform(plan, input, output, workspace, 0, 1, cancellation.Token));
            Assert.All(output, value => Assert.Equal(17, value));
            Assert.All(workspace, value => Assert.Equal(19, value));
        }
    }
}
