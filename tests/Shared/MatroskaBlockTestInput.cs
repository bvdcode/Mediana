// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.Matroska;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    internal static class MatroskaBlockTestInput
    {
        public static async Task VerifyLacingAsync()
        {
            byte[] payload = new byte[2300];
            new Random(794).NextBytes(payload);
            await CheckAsync([0x81, 0, 0, 0x82, 2, 255, 255, 255, 35, 255, 245, .. payload], [800, 500, 1000], 11, MatroskaLacingMode.Xiph);
            await CheckAsync([0x81, 0, 0, 0x86, 2, 0x43, 0x20, 0x5E, 0xD3, .. payload], [800, 500, 1000], 9, MatroskaLacingMode.Ebml);
            await CheckAsync([0x81, 0, 0, 0x84, 2, .. new byte[2400]], [800, 800, 800], 5, MatroskaLacingMode.FixedSize);
            await CheckAsync([0x81, 0, 0, 0x86, 2, 0x81, 0xFF, .. new byte[68]], [1, 65, 2], 7, MatroskaLacingMode.Ebml);
            await CheckAsync([0x81, 0, 0, 0x86, 1, 0xFF, .. new byte[128]], [127, 1], 6, MatroskaLacingMode.Ebml);
            await CheckAsync([0x81, 0, 0, 0x82, 1, 255, 0, .. new byte[256]], [255, 1], 7, MatroskaLacingMode.Xiph);
            await CheckAsync([0x81, 0, 0, 0x86, 1, 0x80, 42], [0, 1], 6, MatroskaLacingMode.Ebml);
            byte[] wideSizes = [1, 0, 0, 0, 0, 0, 0, 1];
            for (int index = 0; index < 31; index++)
            {
                wideSizes = [.. wideSizes, 1, 0x7F, 255, 255, 255, 255, 255, 255];
            }
            await CheckAsync([0x81, 0, 0, 0x86, 32, .. wideSizes, .. new byte[33]],
                Enumerable.Repeat(1, 33).ToArray(), 261, MatroskaLacingMode.Ebml);
            foreach (byte mode in new byte[] { 1, 2, 3 })
            {
                byte[] sizes = mode switch
                {
                    1 => Enumerable.Repeat((byte)1, 255).ToArray(),
                    2 => [],
                    3 => [0x81, .. Enumerable.Repeat((byte)0xBF, 254)],
                    _ => throw new InvalidOperationException()
                };
                byte[] block = [0x81, 0, 0, (byte)(0x80 | (mode << 1)), 255, .. sizes, .. new byte[256]];
                await CheckAsync(block, Enumerable.Repeat(1, 256).ToArray(), 5 + sizes.Length, (MatroskaLacingMode)mode);
            }
        }

        public static async Task VerifyIntegersAndFlagsAsync()
        {
            for (int width = 1; width <= 8; width++)
            {
                ulong number = (1UL << (7 * width)) - 1;
                byte[] integer = new byte[width];
                integer.AsSpan().Fill(255);
                integer[0] = (byte)(255 >> (width - 1));
                byte[] input = [.. integer, 0x80, 0, 0x89, 11, 22];
                long start = long.MaxValue - input.Length;
                await using EbmlElementTestSource source = new(input) { BaseOffset = start, Length = long.MaxValue, MaximumRead = 1 };
                MediaBufferAllocator allocator = new(3328);
                using (MatroskaBlockReader reader = new(source, allocator, 64, 1024))
                {
                    MatroskaBlockHeader header = await reader.ReadAsync(new(0xA3, start, long.MaxValue));
                    Require(header.TrackNumber == number && header.RelativeTimestamp == short.MinValue && header.IsKeyFrame == true);
                    Require(header.IsInvisible && header.Flags == 0x89 && header.FrameCount == 1);
                    Require(reader.GetFrame(0) == new MatroskaFrameRange(long.MaxValue - 2, 2));
                    Reject<ArgumentOutOfRangeException>(() => reader.GetFrame(-1));
                    Reject<ArgumentOutOfRangeException>(() => reader.GetFrame(1));
                }
                Require(allocator.AllocatedBytes == 0 && !source.IsDisposed);
            }
            byte[] grouped = [0x81, 0x7F, 0xFF, 9, 42];
            await using EbmlElementTestSource groupSource = new(grouped);
            using MatroskaBlockReader groupReader = new(groupSource, new MediaBufferAllocator(3328), 64, 1024);
            MatroskaBlockHeader blockHeader = await groupReader.ReadAsync(new(0xA1, 0, grouped.Length));
            Require(!blockHeader.IsSimpleBlock && blockHeader.IsKeyFrame is null && blockHeader.IsInvisible);
            Require(blockHeader.RelativeTimestamp == short.MaxValue && groupReader.GetFrame(0).Length == 1);
        }

        public static async Task VerifyInvalidLayoutsAsync()
        {
            foreach (byte[] input in new byte[][]
            {
                [0x80, 0, 0, 0], [0, 0, 0, 0], [0x81, 0, 0, 0x10],
                [0x81, 0, 0, 0x82, 0], [0x81, 0, 0, 0x84, 0], [0x81, 0, 0, 0x86, 0],
                [0x81, 0, 0, 0x84, 1, 0], [0x81, 0, 0, 0x82, 1, 3, 1, 2],
                [0x81, 0, 0, 0x86, 2, 0x81, 0xBD, 1, 2],
                [0x81, 0, 0, 0x86, 1, 0x83, 1, 2]
            })
            {
                await RejectInputAsync<InvalidDataException>(input);
            }
            byte[] maximum = [1, 255, 255, 255, 255, 255, 255, 255, 0, 0, 0x80];
            for (int count = 0; count < maximum.Length; count++)
            {
                await RejectInputAsync<EndOfStreamException>(maximum[..count]);
            }
            await RejectInputAsync<EndOfStreamException>([0x81, 0, 0, 0x82, 1, 255]);
            await RejectInputAsync<EndOfStreamException>([0x81, 0, 0, 0x86, 1, 0x40]);
            await RejectInputAsync<NotSupportedException>([0x81, 0, 0, 0x80, .. new byte[1025]]);
            await RejectInputAsync<NotSupportedException>([0x81, 0, 0, 0x82, 1, 255, 255, 255, 255, 5]);
            byte[] reserved = [0x81, 0, 0, 0x80];
            await using EbmlElementTestSource source = new(reserved);
            MediaBufferAllocator allocator = new(3328);
            using (MatroskaBlockReader reader = new(source, allocator, 64, 1024))
            {
                await RejectAsync<InvalidDataException>(async () => await reader.ReadAsync(new(0xA1, 0, reserved.Length)));
                await RejectAsync<InvalidDataException>(async () => await reader.ReadAsync(new(0xA3, 0, null)));
                await RejectAsync<InvalidDataException>(async () => await reader.ReadAsync(new(0xA3, 0, 100)));
                await RejectAsync<ArgumentException>(async () => await reader.ReadAsync(new(0xA0, 0, reserved.Length)));
                Reject<InvalidOperationException>(() => reader.GetFrame(0));
            }
            Require(allocator.AllocatedBytes == 0 && !source.IsDisposed);
        }

        public static async Task VerifyCancellationAndLimitsAsync()
        {
            byte[] input = [0x81, 0, 0, 0x82, 1, .. Enumerable.Repeat((byte)255, 600), 0, .. new byte[153001]];
            TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            await using EbmlElementTestSource source = new(input) { ReadGate = gate.Task, ReadGateOffset = 256, MaximumRead = 3 };
            MediaBufferAllocator allocator = new(3328);
            using (MatroskaBlockReader reader = new(source, allocator, 1024, 200000))
            {
                Reject<InvalidOperationException>(() => reader.GetFrame(0));
                using CancellationTokenSource canceled = new();
                await canceled.CancelAsync();
                await RejectAsync<OperationCanceledException>(async () => await reader.ReadAsync(new(0xA3, 0, input.Length), canceled.Token));
                Require(source.ReadCalls == 0);
                using CancellationTokenSource pending = new();
                ValueTask<MatroskaBlockHeader> read = reader.ReadAsync(new(0xA3, 0, input.Length), pending.Token);
                Require(!read.IsCompleted);
                await pending.CancelAsync();
                await RejectAsync<OperationCanceledException>(async () => await read);
                Reject<InvalidOperationException>(() => reader.GetFrame(0));
                gate.SetResult();
                MatroskaBlockHeader header = await reader.ReadAsync(new(0xA3, 0, input.Length));
                Require(header.FrameCount == 2 && header.PayloadOffset == 606);
                Require(reader.GetFrame(0) == new MatroskaFrameRange(606, 153000));
                Require(reader.GetFrame(1) == new MatroskaFrameRange(153606, 1));
                await RejectAsync<OperationCanceledException>(async () => await reader.ReadAsync(new(0xA3, 0, input.Length), canceled.Token));
                Reject<InvalidOperationException>(() => reader.GetFrame(0));
            }
            Require(allocator.AllocatedBytes == 0 && !source.IsDisposed);
            using (MatroskaBlockReader limited = new(source, allocator, 512, 200000))
            {
                await RejectAsync<NotSupportedException>(async () => await limited.ReadAsync(new(0xA3, 0, input.Length)));
            }
            Require(allocator.AllocatedBytes == 0);
            Reject<MediaMemoryLimitException>(() => new MatroskaBlockReader(source, new MediaBufferAllocator(3327), 64, 1024));
            using MatroskaBlockReader disposed = new(source, allocator, 64, 1024);
            disposed.Dispose();
            await RejectAsync<ObjectDisposedException>(async () => await disposed.ReadAsync(new(0xA3, 0, input.Length)));
            Reject<ObjectDisposedException>(() => disposed.GetFrame(0));
            Require(allocator.AllocatedBytes == 0);
        }

        private static async Task CheckAsync(byte[] input, int[] sizes, int start, MatroskaLacingMode mode)
        {
            await using EbmlElementTestSource source = new(input) { MaximumRead = 1 };
            MediaBufferAllocator allocator = new(3328);
            using (MatroskaBlockReader reader = new(source, allocator, 1024, 200000))
            {
                MatroskaBlockHeader header = await reader.ReadAsync(new(0xA3, 0, input.Length));
                Require(header.TrackNumber == 1 && header.FrameCount == sizes.Length && header.PayloadOffset == start && header.Lacing == mode);
                long offset = start;
                for (int index = 0; index < sizes.Length; index++)
                {
                    Require(reader.GetFrame(index) == new MatroskaFrameRange(offset, sizes[index]));
                    offset += sizes[index];
                }
                Require(offset == input.Length && allocator.AllocatedBytes == 3328);
            }
            Require(allocator.AllocatedBytes == 0 && !source.IsDisposed);
        }

        private static async Task RejectInputAsync<TException>(byte[] input) where TException : Exception
        {
            await using EbmlElementTestSource source = new(input);
            MediaBufferAllocator allocator = new(3328);
            using (MatroskaBlockReader reader = new(source, allocator, 1024, 1024))
            {
                await RejectAsync<TException>(async () => await reader.ReadAsync(new(0xA3, 0, input.Length)));
                Reject<InvalidOperationException>(() => reader.GetFrame(0));
            }
            Require(allocator.AllocatedBytes == 0 && !source.IsDisposed);
        }

        private static void Require(bool condition)
        {
            if (!condition)
            {
                throw new InvalidDataException("Matroska block framing, lacing or storage ownership changed.");
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
            throw new InvalidDataException("An invalid Matroska block operation was accepted.");
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
            throw new InvalidDataException("An invalid Matroska block operation was accepted.");
        }
    }
}
