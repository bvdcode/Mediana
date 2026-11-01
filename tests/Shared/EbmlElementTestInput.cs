// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.Matroska;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    internal static class EbmlElementTestInput
    {
        public static async Task VerifyBoundsAsync()
        {
            byte[] bytes = [0xA0, 0x86, 0x81, 0x80, 0x82, 0x82, 0x12, 0x34, 0x83, 0x80];
            await using EbmlElementTestSource source = new(bytes) { MaximumRead = 1, ReadBoundary = 8 };
            MediaBufferAllocator allocator = new(12);
            using (EbmlElementReader reader = new(source, allocator))
            {
                EbmlElement parent = await reader.ReadAsync(0, 8) ?? throw new InvalidDataException("Missing EBML parent.");
                Require(parent.Id == 0xA0 && parent.DataOffset == 2 && parent.EndOffset == 8 && parent.DataSize == 6);
                EbmlElement first = await reader.ReadAsync(2, parent.EndOffset) ?? throw new InvalidDataException("Missing EBML child.");
                Require(first.Id == 0x81 && first.DataOffset == 4 && first.EndOffset == 4);
                EbmlElement second = await reader.ReadAsync(4, parent.EndOffset) ?? throw new InvalidDataException("Missing EBML child.");
                Require(second.Id == 0x82 && second.DataOffset == 6 && second.DataSize == 2 && second.EndOffset == 8);
                int calls = source.ReadCalls;
                Require(await reader.ReadAsync(8, 8) is null && source.ReadCalls == calls);
                source.ReadBoundary = 10;
                EbmlElement sibling = await reader.ReadAsync(8, 10) ?? throw new InvalidDataException("Missing EBML sibling.");
                Require(sibling.Id == 0x83 && sibling.DataOffset == 10 && sibling.EndOffset == 10);
                Require(allocator.AllocatedBytes == 12);
                await RejectAsync<InvalidDataException>(async () => await reader.ReadAsync(9, 8));
                await RejectAsync<InvalidDataException>(async () => await reader.ReadAsync(0, 11));
                await RejectAsync<ArgumentOutOfRangeException>(async () => await reader.ReadAsync(-1, 8));
            }
            Require(allocator.AllocatedBytes == 0 && !source.IsDisposed);
            using EbmlElementReader disposed = new(source, allocator);
            disposed.Dispose();
            await RejectAsync<ObjectDisposedException>(async () => await disposed.ReadAsync(0, 8));
            Require(allocator.AllocatedBytes == 0 && !source.IsDisposed);
        }

        public static async Task VerifyTruncationAsync()
        {
            byte[] full = [0x1A, 0x45, 0xDF, 0xA3, 1, 0, 0, 0, 0, 0, 0, 0];
            for (int length = 1; length < full.Length; length++)
            {
                await using EbmlElementTestSource source = new(full.AsMemory(0, length)) { MaximumRead = 1 };
                MediaBufferAllocator allocator = new(12);
                using (EbmlElementReader reader = new(source, allocator))
                {
                    await RejectAsync<EndOfStreamException>(async () => await reader.ReadAsync(0, length));
                }
                Require(allocator.AllocatedBytes == 0);
            }
            await using EbmlElementTestSource unknown = new(new byte[] { 0x81, 0x80 }) { Length = null, MaximumRead = 1 };
            MediaBufferAllocator storage = new(12);
            using EbmlElementReader unknownReader = new(unknown, storage);
            Require((await unknownReader.ReadAsync(0, null))?.DataSize == 0);
            Require(await unknownReader.ReadAsync(2, null) is null);
            await using EbmlElementTestSource nearLimit = new(new byte[] { 0x81, 0x80 })
            {
                BaseOffset = long.MaxValue - 2,
                Length = long.MaxValue,
                MaximumRead = 1
            };
            using EbmlElementReader nearReader = new(nearLimit, new MediaBufferAllocator(12));
            Require((await nearReader.ReadAsync(long.MaxValue - 2, long.MaxValue))?.EndOffset == long.MaxValue);
            MediaBufferAllocator largeStorage = new(12);
            await using EbmlElementTestSource large = new(new byte[] { 0xA0, 1, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFE }) { Length = null };
            using (EbmlElementReader largeReader = new(large, largeStorage))
            {
                Require((await largeReader.ReadAsync(0, null))?.DataSize == 72057594037927934);
                Require(largeStorage.AllocatedBytes == 12);
            }
            Require(largeStorage.AllocatedBytes == 0);
            EbmlElement unbounded = EbmlElement.ReadHeader([0xA0, 0xFF], 10, 20);
            Require(unbounded.Id == 0xA0 && unbounded.DataOffset == 12 && unbounded.EndOffset is null && unbounded.DataSize is null);
            EbmlElement last = EbmlElement.ReadHeader([0x81, 0x80], long.MaxValue - 2, long.MaxValue);
            Require(last.DataOffset == long.MaxValue && last.EndOffset == long.MaxValue);
            Reject<InvalidDataException>(() => EbmlElement.ReadHeader([0x81, 0x81], long.MaxValue - 2, null));
            Reject<InvalidDataException>(() => EbmlElement.ReadHeader([0x81, 0x80], long.MaxValue - 1, null));
            Reject<InvalidDataException>(() => EbmlElement.ReadHeader([0x81, 0x85], 0, 4));
            Reject<InvalidDataException>(() => EbmlElement.ReadHeader([0x81, 0x80], 0, 1));
            Reject<InvalidDataException>(() => EbmlElement.ReadHeader([0x81, 0x80], 0, -1));
        }

        public static async Task VerifyFailureAndCancellationAsync()
        {
            foreach (int count in new[] { -1, 13 })
            {
                await using EbmlElementTestSource broken = new(new byte[] { 0x81, 0x80 }) { ReadCountOverride = count };
                MediaBufferAllocator storage = new(12);
                using (EbmlElementReader reader = new(broken, storage))
                {
                    await RejectAsync<InvalidDataException>(async () => await reader.ReadAsync(0, null));
                }
                Require(storage.AllocatedBytes == 0);
            }
            TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            await using EbmlElementTestSource source = new(new byte[] { 0x81, 0x80 }) { ReadGate = gate.Task };
            MediaBufferAllocator allocator = new(12);
            using EbmlElementReader cancelReader = new(source, allocator);
            using CancellationTokenSource canceled = new();
            await canceled.CancelAsync();
            await RejectAsync<OperationCanceledException>(async () => await cancelReader.ReadAsync(0, null, canceled.Token));
            Require(source.ReadCalls == 0);
            using CancellationTokenSource pending = new();
            ValueTask<EbmlElement?> read = cancelReader.ReadAsync(0, null, pending.Token);
            Require(!read.IsCompleted && source.ReadCalls == 1);
            await pending.CancelAsync();
            await RejectAsync<OperationCanceledException>(async () => await read);
            gate.SetResult();
            Require((await cancelReader.ReadAsync(0, null))?.Id == 0x81);
            MediaBufferAllocator insufficient = new(11);
            Reject<MediaMemoryLimitException>(() => new EbmlElementReader(source, insufficient));
            Require(insufficient.AllocatedBytes == 0 && !source.IsDisposed);
            await using EbmlElementTestSource limited = new(new byte[] { 0x81, 0x40, 0 }) { MaximumRead = 1 };
            using EbmlElementReader widthReader = new(limited, new MediaBufferAllocator(12), maximumSizeWidth: 1);
            await RejectAsync<InvalidDataException>(async () => await widthReader.ReadAsync(0, null));
        }

        private static void Require(bool condition)
        {
            if (!condition)
            {
                throw new InvalidDataException("An EBML element changed its bounds, storage or source ownership.");
            }
        }

        private static async Task RejectAsync<TException>(Func<Task> action) where TException : Exception
        {
            try
            {
                await action();
            }
            catch (TException)
            {
                return;
            }
            throw new InvalidDataException("An invalid EBML element operation was accepted.");
        }

        private static void Reject<TException>(Action action) where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                return;
            }
            throw new InvalidDataException("An invalid EBML element was accepted.");
        }
    }
}
