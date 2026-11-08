// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.CompilerServices;
using Mediana.Core.Memory;

namespace Mediana.Codecs.Av1.Tests
{
    public class Av1StillPictureFailureTests
    {
        [Fact]
        public void EveryTruncatedObuExtentFailsBeforePublishingPixels()
        {
            byte[] encoded = Read("palette");
            MediaBufferAllocator allocator = new(1024 * 1024);
            for (int length = 0; length < encoded.Length; length++)
            {
                int prefix = length;
                Assert.Throws<InvalidDataException>(() => Decode(encoded.AsSpan(0, prefix), allocator));
                Assert.Equal(0, allocator.AllocatedBytes);
            }
        }

        [Fact]
        public void InvalidEntropyTerminationReleasesPixelsAndWorkspace()
        {
            byte[] encoded = Read("palette");
            encoded[^1] |= 1;
            MediaBufferAllocator allocator = new(1024 * 1024);
            Assert.Throws<InvalidDataException>(() => Decode(encoded, allocator));
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void ScopedBudgetFailureReleasesEveryPriorAllocation(int allocation)
        {
            byte[] encoded = Read("palette");
            int samples = 16 * 16;
            int contextBytes = (4 + Av1TileDecoder.LeftContexts) * Unsafe.SizeOf<Av1BlockContext>();
            long limit = allocation switch
            {
                0 => samples - 1,
                1 => samples,
                2 => samples + contextBytes,
                _ => throw new ArgumentOutOfRangeException(nameof(allocation))
            };
            MediaBufferAllocator parent = new(1024 * 1024);
            MediaBufferAllocator allocator = parent.CreateScope(limit);
            Assert.Throws<MediaMemoryLimitException>(() => Decode(encoded, allocator));
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.Equal(0, parent.AllocatedBytes);
        }

        [Fact]
        public void CancellationBeforeDecodeDoesNotAllocate()
        {
            byte[] encoded = Read("palette");
            MediaBufferAllocator allocator = new(1024 * 1024);
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();
            Assert.Throws<OperationCanceledException>(() => Decode(encoded, allocator, cancellation.Token));
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void PixelsAreIndependentOfBorrowedEncodedInput()
        {
            byte[] encoded = Read("palette");
            byte[] expected = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", "palette.gray"));
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (Av1Picture picture = Av1StillPictureDecoder.Decode(encoded, allocator))
            {
                Array.Clear(encoded);
                Assert.True(expected.AsSpan().SequenceEqual(picture.Pixels.Memory.Span));
                Assert.Equal(expected.Length, allocator.AllocatedBytes);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task ConcurrentPicturesUseIndependentArithmeticAndPredictionState()
        {
            byte[] encoded = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "noise16.obu"));
            byte[] expected = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "noise16.gray"));
            MediaBufferAllocator allocator = new(1024 * 1024);
            Task[] operations = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
            {
                for (int ordinal = 0; ordinal < 8; ordinal++)
                {
                    using Av1Picture picture = Av1StillPictureDecoder.Decode(encoded, allocator);
                    Assert.True(expected.AsSpan().SequenceEqual(picture.Pixels.Memory.Span));
                }
            })).ToArray();
            await Task.WhenAll(operations);
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        private static byte[] Read(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", name + ".obu"));

        private static void Decode(ReadOnlySpan<byte> encoded, MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            using Av1Picture picture = Av1StillPictureDecoder.Decode(encoded, allocator, cancellationToken);
        }
    }
}
