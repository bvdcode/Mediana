// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.IO;

namespace Mediana.Tests
{
    [Collection("ImageAllocation")]
    public class BoundedStreamReaderTests
    {
        [Fact]
        public async Task EveryFragmentAndSegmentBoundaryPreservesBytesAndIndependentOwnership() =>
            await BoundedStreamReadTestInput.VerifyAsync();

        [Fact]
        public void SynchronousFragmentAndSegmentBoundariesPreserveOwnedBytes() =>
            BoundedStreamReadTestInput.VerifySynchronous();

        [Fact]
        public async Task WholePayloadAllocationExcludesGeometricGrowthCopies() =>
            await BoundedStreamReadTestInput.VerifyAllocationAsync();

        [Fact]
        public void SynchronousPayloadAllocationExcludesGeometricGrowthCopies() =>
            BoundedStreamReadTestInput.VerifyAllocationSynchronous();

        [Theory]
        [InlineData(0)]
        [InlineData(65536)]
        public async Task ExceedingTheLimitRejectsAnExtraByteAndLeavesTheSourceOpen(int limit)
        {
            using FragmentedReadStream sync = new(new byte[limit + 1], 19);
            Assert.Throws<InvalidDataException>(() => BoundedStreamReader.Read(sync, limit));
            Assert.True(sync.CanRead);
            using FragmentedReadStream asyncSource = new(new byte[limit + 1], 19);
            await Assert.ThrowsAsync<InvalidDataException>(() => BoundedStreamReader.ReadAsync(asyncSource, limit));
            Assert.True(asyncSource.CanRead);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(9)]
        public async Task InvalidStreamReadCountsAreRejected(int reported)
        {
            using FragmentedReadStream sync = new([], 1) { ReportedCount = reported };
            Assert.Throws<InvalidDataException>(() => BoundedStreamReader.Read(sync, 8));
            using FragmentedReadStream asyncSource = new([], 1) { ReportedCount = reported };
            await Assert.ThrowsAsync<InvalidDataException>(() => BoundedStreamReader.ReadAsync(asyncSource, 8));
        }

        [Fact]
        public async Task CancellationAndSourceFailuresRemainTerminalWithoutClosingTheSource()
        {
            using CancellationTokenSource cancellation = new();
            using FragmentedReadStream sync = new(new byte[32], 1) { BeforeRead = cancellation.Cancel };
            Assert.Throws<OperationCanceledException>(() => BoundedStreamReader.Read(sync, 64, cancellation.Token));
            Assert.True(sync.CanRead);
            using CancellationTokenSource asyncCancellation = new();
            using FragmentedReadStream asyncSource = new(new byte[32], 1) { BeforeRead = asyncCancellation.Cancel };
            await Assert.ThrowsAsync<OperationCanceledException>(() => BoundedStreamReader.ReadAsync(asyncSource, 64, asyncCancellation.Token));
            Assert.True(asyncSource.CanRead);
            IOException failure = new("Read failure.");
            using FragmentedReadStream broken = new([], 1) { BeforeRead = () => throw failure };
            Assert.Same(failure, Assert.Throws<IOException>(() => BoundedStreamReader.Read(broken, 64)));
            Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => BoundedStreamReader.ReadAsync(broken, 64)));
            Assert.True(broken.CanRead);
        }

        [Fact]
        public async Task InvalidLimitsAndNullSourcesAreRejected()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(() => BoundedStreamReader.ReadAsync(null!, 8));
            using MemoryStream source = new();
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => BoundedStreamReader.ReadAsync(source, -1));
            Assert.Empty(await BoundedStreamReader.ReadAsync(source, int.MaxValue));
        }

        [Fact]
        public void SynchronousLimitsAndNullSourcesAreChecked()
        {
            Assert.Throws<ArgumentNullException>(() => BoundedStreamReader.Read(null!, 8));
            using MemoryStream source = new();
            Assert.Throws<ArgumentOutOfRangeException>(() => BoundedStreamReader.Read(source, -1));
            Assert.Empty(BoundedStreamReader.Read(source, int.MaxValue));
        }
    }
}
