// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Codecs.Av1.Tests
{
    public class Av1GrainSynthesisTests
    {
        [Fact]
        public void MatchesIndependentSynthesisAcrossBitDepthsAndGrainParameters() =>
            Av1GrainKernelTestInput.Verify(Path.Combine(AppContext.BaseDirectory, "Data"));

        [Fact]
        public void RejectedBudgetAndCancellationRetainNoScratch()
        {
            Av1GrainParameters parameters = Parameters();
            MediaBufferAllocator allocator = new(1);
            byte[] source = new byte[1024], target = new byte[1024];
            PlaneLayout[] planes = [new(0, 32, 32, 32)];
            Assert.Throws<MediaMemoryLimitException>(() => Av1GrainSynthesis.Apply(parameters, 32, 32, true, false, source, planes, target, planes, allocator, default));
            Assert.Equal(0, allocator.AllocatedBytes);
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();
            Assert.Throws<OperationCanceledException>(() => Av1GrainSynthesis.Apply(parameters, 32, 32, true, false, source, planes, target, planes, allocator, cancellation.Token));
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.All(target, value => Assert.Equal(0, value));
        }

        [Fact]
        public void RejectsAliasedReferencesAndInvalidPlaneGeometry()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            byte[] source = new byte[1024], target = new byte[1024];
            PlaneLayout[] planes = [new(0, 32, 32, 32)];
            Assert.Throws<ArgumentException>(() => Av1GrainSynthesis.Apply(Parameters(), 32, 32, true, false, source, planes, source, planes, allocator, default));
            PlaneLayout[] invalid = [new(0, 32, 31, 32)];
            Assert.Throws<ArgumentException>(() => Av1GrainSynthesis.Apply(Parameters(), 32, 32, true, false, source, invalid, target, planes, allocator, default));
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task IndependentCallsShareOnlyTheAllocator()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            Av1GrainParameters parameters = Parameters();
            byte[] source = Enumerable.Repeat((byte)128, 1024).ToArray();
            byte[] snapshot = source.ToArray();
            PlaneLayout[] planes = [new(0, 32, 32, 32)];
            Task<byte[]>[] calls = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
            {
                byte[] output = new byte[source.Length];
                Av1GrainSynthesis.Apply(parameters, 32, 32, true, false, source, planes, output, planes, allocator, default);
                return output;
            })).ToArray();
            byte[][] results = await Task.WhenAll(calls);
            Assert.All(results, output => Assert.Equal(results[0], output));
            Assert.Equal(snapshot, source);
            Assert.NotEqual(source, results[0]);
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        private static Av1GrainParameters Parameters() =>
            new(123, new Av1GrainPoint[] { new(0, 96), new(255, 96) }, ReadOnlyMemory<sbyte>.Empty,
                default, default, false, 8, 0, 6, 0, true, false);
    }
}
