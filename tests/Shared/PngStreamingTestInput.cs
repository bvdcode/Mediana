// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Codecs.Png;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    internal static class PngStreamingTestInput
    {
        private const int ScratchBytes = 64 * 1024 + 12;

        public static async Task VerifyRoundtripAsync(int width, int height, bool noisy)
        {
            MediaBufferAllocator inputBudget = new((long)(width * 4 + 7) * height + 3);
            using VideoFrame frame = Frame(inputBudget, width, height, noisy);
            byte[] original = frame.Data.ToArray();
            MediaBufferAllocator scratch = new(ScratchBytes);
            using PngOutputTestStream output = new();
            output.BeforeWrite = async (_, bytes, _) =>
            {
                byte[] borrowed = bytes.ToArray();
                await Task.Yield();
                if (scratch.AllocatedBytes != ScratchBytes || !bytes.Span.SequenceEqual(borrowed))
                {
                    throw new InvalidOperationException("PNG scratch storage changed while a write was pending.");
                }
            };
            long written = await PngEncoder.EncodeAsync(frame, output, scratch);
            byte[] png = output.ToArray();
            Dictionary<string, byte[]> actual = PngTestInput.ReadChunks(png);
            MediaBufferAllocator buffered = new(original.Length * 2L + 1024);
            using (MediaBuffer reference = PngEncoder.Encode(frame, buffered))
            {
                Dictionary<string, byte[]> expected = PngTestInput.ReadChunks(reference.Memory.Span);
                foreach (string type in expected.Keys)
                {
                    if (!actual[type].AsSpan().SequenceEqual(expected[type]))
                    {
                        throw new InvalidDataException($"Streaming PNG changed the {type} contents.");
                    }
                }
            }
            byte[] pixels = PngTestInput.Inflate(actual["IDAT"]);
            for (int row = 0; row < height; row++)
            {
                int offset = row * (width * 4 + 1);
                if (pixels[offset] != 0 || !pixels.AsSpan(offset + 1, width * 4).SequenceEqual(frame.GetRow(0, row)))
                {
                    throw new InvalidDataException("Streaming PNG changed visible samples or padding.");
                }
            }
            int chunks = 0;
            for (int offset = 8; offset < png.Length;)
            {
                int length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset));
                if (png.AsSpan(offset + 4, 4).SequenceEqual("IDAT"u8))
                {
                    if (length is <= 0 or > 64 * 1024)
                    {
                        throw new InvalidDataException("An IDAT chunk exceeded the bounded payload size.");
                    }
                    chunks++;
                }
                offset += length + 12;
            }
            int expectedChunks = (actual["IDAT"].Length + 65535) / 65536;
            if (written != png.Length || chunks != expectedChunks || scratch.AllocatedBytes != 0 ||
                buffered.AllocatedBytes != 0 || !output.CanWrite || !frame.Data.Span.SequenceEqual(original))
            {
                throw new InvalidOperationException("PNG streaming length, ownership or scratch budget is invalid.");
            }
        }

        public static async Task VerifyPendingLifetimeAsync()
        {
            MediaBufferAllocator inputBudget = new(14);
            using VideoFrame frame = Frame(inputBudget, 1, 1, false);
            MediaBufferAllocator scratch = new(ScratchBytes);
            using PngOutputTestStream output = new();
            TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            output.BeforeWrite = async (index, _, token) =>
            {
                if (index == 1)
                {
                    frame.Dispose();
                    entered.SetResult();
                    await release.Task.WaitAsync(token);
                }
            };
            Task<long> encoding = PngEncoder.EncodeAsync(frame, output, scratch);
            await entered.Task;
            bool retained = inputBudget.AllocatedBytes == 14 && scratch.AllocatedBytes == ScratchBytes && !encoding.IsCompleted;
            release.SetResult();
            await encoding;
            if (!retained || inputBudget.AllocatedBytes != 0 || scratch.AllocatedBytes != 0 || !output.CanWrite)
            {
                throw new InvalidOperationException("A pending PNG write did not retain and release its owners.");
            }
            PngTestInput.ReadChunks(output.ToArray());
        }

        public static async Task VerifyFailuresAsync(bool cancel)
        {
            MediaBufferAllocator inputBudget = new((512 * 4 + 7) * 512 + 3);
            using VideoFrame frame = Frame(inputBudget, 512, 512, true);
            using PngOutputTestStream reference = new();
            MediaBufferAllocator scratch = new(ScratchBytes);
            await PngEncoder.EncodeAsync(frame, reference, scratch);
            foreach (int failAt in new[] { 1, 2, reference.Writes - 1, reference.Writes })
            {
                using CancellationTokenSource cancellation = new();
                using PngOutputTestStream output = new();
                IOException expected = new("PNG output rejected a write.");
                output.BeforeWrite = async (index, _, token) =>
                {
                    if (index == failAt)
                    {
                        if (cancel)
                        {
                            await cancellation.CancelAsync();
                            token.ThrowIfCancellationRequested();
                        }
                        throw expected;
                    }
                };
                bool rejected = false;
                try
                {
                    await PngEncoder.EncodeAsync(frame, output, scratch, cancellation.Token);
                }
                catch (IOException actual) when (!cancel && ReferenceEquals(actual, expected))
                {
                    rejected = true;
                }
                catch (OperationCanceledException actual) when (cancel && actual.CancellationToken == cancellation.Token)
                {
                    rejected = true;
                }
                if (!rejected || output.Writes != failAt || scratch.AllocatedBytes != 0 || !output.CanWrite || frame.Data.Length == 0)
                {
                    throw new InvalidOperationException("Failed PNG output was continued, closed, leaked or masked its original error.");
                }
            }
        }

        public static async Task VerifyEarlyRejectionAsync()
        {
            MediaBufferAllocator inputBudget = new(14);
            using VideoFrame frame = Frame(inputBudget, 1, 1, false);
            using PngOutputTestStream output = new();
            MediaBufferAllocator scratch = new(ScratchBytes - 1);
            await RequireFailureAsync<MediaMemoryLimitException>(() => PngEncoder.EncodeAsync(frame, output, scratch));
            await RequireFailureAsync<OperationCanceledException>(() => PngEncoder.EncodeAsync(frame, output, new(ScratchBytes), new(true)));
            using MemoryStream readOnly = new([], writable: false);
            await RequireFailureAsync<ArgumentException>(() => PngEncoder.EncodeAsync(frame, readOnly, scratch));
            if (output.Writes != 0 || scratch.AllocatedBytes != 0 || inputBudget.AllocatedBytes != 14)
            {
                throw new InvalidOperationException("PNG output must validate and reserve bounded working storage before writing.");
            }
            frame.Dispose();
            await RequireFailureAsync<ObjectDisposedException>(() => PngEncoder.EncodeAsync(frame, output, new(ScratchBytes)));
            if (output.Writes != 0 || inputBudget.AllocatedBytes != 0)
            {
                throw new InvalidOperationException("Disposed PNG input must be rejected before writing.");
            }
        }

        private static async Task RequireFailureAsync<TException>(Func<Task> operation) where TException : Exception
        {
            bool rejected = false;
            try
            {
                await operation();
            }
            catch (TException)
            {
                rejected = true;
            }
            if (!rejected)
            {
                throw new InvalidOperationException("Invalid PNG output unexpectedly succeeded.");
            }
        }

        private static VideoFrame Frame(MediaBufferAllocator allocator, int width, int height, bool noisy)
        {
            int stride = width * 4 + 7;
            using WritableMediaBuffer writable = allocator.Allocate(stride * height + 3);
            if (noisy)
            {
                new Random(7621).NextBytes(writable.Memory.Span);
            }
            else
            {
                writable.Memory.Span.Fill(173);
            }
            using MediaBuffer pixels = writable.Publish();
            VideoFormat format = new(width, height, VideoPixelFormat.Rgba32, VideoColorInfo.Srgb, new SampleAspectRatio(4, 3));
            return new VideoFrame(format, pixels, [new PlaneLayout(3, stride, width * 4, height)]);
        }
    }
}
