// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.Processing;

namespace Mediana.Tests
{
    internal static class VideoWindowTestInput
    {
        public static async Task VerifySelectionAsync()
        {
            foreach ((long start, long? end, int[] expected, int reads) in new (long, long?, int[], int)[]
            {
                (0, 25, [0, 1, 2], 4), (1, 25, [1, 2], 4), (10, 25, [1, 2], 4),
                (0, 10, [0], 2), (10, 10, [], 0), (-20, 0, [], 1),
                (30, 100, [3], 5), (40, 100, [], 5), (20, null, [2, 3], 5)
            })
            {
                MediaBufferAllocator allocator = new(4);
                int position = 0;
                int calls = 0;
                VideoFrame? original = null;
                ValueTask<VideoFrame?> ReadAsync(CancellationToken token)
                {
                    token.ThrowIfCancellationRequested();
                    calls++;
                    original = position < 4 ? Create(allocator, new(position++ * 10, new(1, 1000))) : null;
                    return ValueTask.FromResult(original);
                }
                await using VideoTrimmer window = new(ReadAsync, new(start, new(1, 1000)),
                    end is long limit ? new MediaTimestamp(limit * 90, new(1, 90000)) : null);
                foreach (int index in expected)
                {
                    using VideoFrame? frame = await window.ReadFrameAsync();
                    Require(frame is not null && ReferenceEquals(frame, original));
                    Require(frame!.Timestamp == new MediaTimestamp(index, new(1, 100)));
                    Require(frame.Duration == new MediaDuration(15, new(1, 1000)) && frame.Data.Span[0] == index * 10);
                    Require(allocator.AllocatedBytes == 4);
                }
                Require(await window.ReadFrameAsync() is null && allocator.AllocatedBytes == 0 && calls == reads);
                Require(await window.ReadFrameAsync() is null && calls == reads);
                await window.DisposeAsync();
                using VideoFrame? remaining = await ReadAsync(default);
                Require(calls == reads + 1);
            }

            MediaBufferAllocator retainedAllocator = new(4);
            VideoFrame sourceFrame = Create(retainedAllocator, new(long.MaxValue, new(1, int.MaxValue)));
            await using VideoTrimmer retainedWindow = new(_ => ValueTask.FromResult<VideoFrame?>(sourceFrame),
                new(long.MinValue, new(int.MaxValue, 1)));
            using VideoFrame? retained = await retainedWindow.ReadFrameAsync();
            await retainedWindow.DisposeAsync();
            Require(ReferenceEquals(sourceFrame, retained) && retained!.Data.Span[0] == unchecked((byte)long.MaxValue));
            retained!.Dispose();
            Require(retainedAllocator.AllocatedBytes == 0);
        }

        public static async Task VerifyFailureAsync()
        {
            foreach (MediaTimestamp? second in new MediaTimestamp?[] { null, new(9, new(1, 1000)), new(1, new(1, 100)) })
            {
                MediaBufferAllocator allocator = new(4);
                int reads = 0;
                ValueTask<VideoFrame?> ReadAsync(CancellationToken _)
                {
                    reads++;
                    return ValueTask.FromResult<VideoFrame?>(Create(allocator, reads == 1 ? new(10, new(1, 1000)) : second));
                }
                await using VideoTrimmer window = new(ReadAsync, new(100, new(1, 1000)));
                if (second is null)
                {
                    await ExpectAsync<NotSupportedException>(async () => { using VideoFrame? frame = await window.ReadFrameAsync(); });
                }
                else
                {
                    await ExpectAsync<InvalidDataException>(async () => { using VideoFrame? frame = await window.ReadFrameAsync(); });
                }
                Require(reads == 2 && allocator.AllocatedBytes == 0);
                await ExpectAsync<InvalidOperationException>(async () => { using VideoFrame? frame = await window.ReadFrameAsync(); });
                Require(reads == 2);
            }

            InvalidDataException expected = new("The video source is truncated.");
            await using VideoTrimmer failed = new(_ => throw expected, new(0, new(1, 1)));
            try
            {
                using VideoFrame? frame = await failed.ReadFrameAsync();
                throw new InvalidOperationException("The source failure was not propagated.");
            }
            catch (InvalidDataException error) when (ReferenceEquals(error, expected))
            {
            }
            await ExpectAsync<InvalidOperationException>(async () => { using VideoFrame? frame = await failed.ReadFrameAsync(); });

            await ExpectAsync<ArgumentNullException>(() => Task.FromResult(new VideoTrimmer(null!, new(0, new(1, 1)))));
            await ExpectAsync<ArgumentException>(() => Task.FromResult(new VideoTrimmer(_ => default, default)));
            await ExpectAsync<ArgumentOutOfRangeException>(() => Task.FromResult(new VideoTrimmer(_ => default,
                new(2, new(1, 1)), new(1, new(1, 1)))));
        }

        public static async Task VerifyCancellationAsync()
        {
            MediaBufferAllocator allocator = new(4);
            foreach (bool returnFrame in new[] { false, true })
            {
                using CancellationTokenSource cancellation = new();
                int calls = 0;
                async ValueTask<VideoFrame?> ReadAsync(CancellationToken _)
                {
                    calls++;
                    VideoFrame? frame = returnFrame ? Create(allocator, new(0, new(1, 1))) : null;
                    await cancellation.CancelAsync();
                    return frame;
                }
                await using VideoTrimmer window = new(ReadAsync, new(0, new(1, 1)));
                await ExpectAsync<OperationCanceledException>(async () => { using VideoFrame? frame = await window.ReadFrameAsync(cancellation.Token); });
                Require(calls == 1 && allocator.AllocatedBytes == 0);
                await ExpectAsync<InvalidOperationException>(async () => { using VideoFrame? frame = await window.ReadFrameAsync(); });
                Require(calls == 1);
            }
            using CancellationTokenSource cancelled = new();
            await cancelled.CancelAsync();
            int sourceReads = 0;
            await using VideoTrimmer unread = new(_ => { sourceReads++; return default; }, new(0, new(1, 1)));
            await ExpectAsync<OperationCanceledException>(async () => { using VideoFrame? frame = await unread.ReadFrameAsync(cancelled.Token); });
            Require(sourceReads == 0);
        }

        public static async Task VerifyConcurrencyAsync()
        {
            MediaBufferAllocator allocator = new(4);
            TaskCompletionSource<VideoFrame?> pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
            await using VideoTrimmer window = new(_ => new(pending.Task), new(0, new(1, 1)));
            ValueTask<VideoFrame?> read = window.ReadFrameAsync();
            await ExpectAsync<InvalidOperationException>(async () => { using VideoFrame? frame = await window.ReadFrameAsync(); });
            await ExpectAsync<InvalidOperationException>(async () => await window.DisposeAsync());
            pending.SetResult(Create(allocator, new(0, new(1, 1))));
            using VideoFrame? selected = await read;
            await window.DisposeAsync();
            Require(selected is not null && selected.Data.Length == 4 && allocator.AllocatedBytes == 4);
            await ExpectAsync<ObjectDisposedException>(async () => { using VideoFrame? frame = await window.ReadFrameAsync(); });
            selected!.Dispose();
            Require(allocator.AllocatedBytes == 0);
        }

        private static VideoFrame Create(MediaBufferAllocator allocator, MediaTimestamp? timestamp)
        {
            VideoFormat format = new(1, 1, VideoPixelFormat.Rgba32);
            using WritableMediaBuffer writable = allocator.Allocate(4);
            writable.Memory.Span.Fill(unchecked((byte)(timestamp?.Value ?? 0)));
            using MediaBuffer buffer = writable.Publish();
            return new(format, buffer, format.CreateTightLayout(), timestamp, new(15, new(1, 1000)));
        }

        private static async Task ExpectAsync<TException>(Func<Task> action) where TException : Exception
        {
            try
            {
                await action();
            }
            catch (TException)
            {
                return;
            }
            throw new InvalidDataException("Video trimming did not report the expected failure.");
        }

        private static void Require(bool condition)
        {
            if (!condition)
            {
                throw new InvalidDataException("Video window selection changed frame identity, exact timing or storage ownership.");
            }
        }
    }
}
