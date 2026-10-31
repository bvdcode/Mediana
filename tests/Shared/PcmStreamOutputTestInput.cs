// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana.Tests
{
    internal static class PcmStreamOutputTestInput
    {
        public static async Task VerifyAsync(AudioSampleFormat sampleFormat, bool littleEndian)
        {
            const int Samples = 32771;
            const int ScratchLimit = 64 * 1024;
            AudioFormat format = new(48000, sampleFormat, AudioStorageLayout.Interleaved, "right", "left", "center");
            int bytes = format.GetBufferSize(Samples);
            MediaBufferAllocator source = new(bytes);
            using AudioFrame frame = AudioSamplePreparationTestInput.CreateFrame(format, Samples, source);
            ReadOnlyMemory<byte> input = frame.GetPlane(0);
            byte[] expected = input.ToArray();
            byte[] original = input.ToArray();
            bool direct = BitConverter.IsLittleEndian == littleEndian || format.BytesPerSample == 1;
            if (!direct)
            {
                for (int offset = 0; offset < expected.Length; offset += format.BytesPerSample)
                {
                    expected.AsSpan(offset, format.BytesPerSample).Reverse();
                }
            }
            MediaBufferAllocator scratch = new(direct ? 1 : ScratchLimit);
            int written = 0;
            int writes = 0;
            using ObservedWriteStream destination = new((memory, token) =>
            {
                token.ThrowIfCancellationRequested();
                Require(memory.Span.SequenceEqual(expected.AsSpan(written, memory.Length)));
                if (direct)
                {
                    Require(memory.Equals(input) && scratch.AllocatedBytes == 0);
                }
                else
                {
                    Require(memory.Length <= ScratchLimit && scratch.AllocatedBytes == ScratchLimit);
                }
                written += memory.Length;
                writes++;
                return ValueTask.CompletedTask;
            });
            if (littleEndian)
            {
                await PcmStreamWriter.WriteLittleEndianAsync(destination, frame, scratch);
            }
            else
            {
                await PcmStreamWriter.WriteBigEndianAsync(destination, frame, scratch);
            }
            Require(written == bytes && writes == (direct ? 1 : (bytes + ScratchLimit - 1) / ScratchLimit));
            Require(!destination.WasDisposed && scratch.AllocatedBytes == 0 && source.AllocatedBytes == bytes);
            Require(frame.GetPlane(0).Span.SequenceEqual(original));
            frame.Dispose();
            Require(source.AllocatedBytes == 0);
        }

        public static async Task VerifyPendingFailureAsync(bool swap, bool cancel)
        {
            AudioFormat format = new(48000, AudioSampleFormat.Float64, AudioStorageLayout.Interleaved, "mono");
            MediaBufferAllocator source = new(format.GetBufferSize(10000));
            MediaBufferAllocator scratch = new(64 * 1024);
            using AudioFrame frame = AudioSamplePreparationTestInput.CreateFrame(format, 10000, source);
            TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            using CancellationTokenSource cancellation = new();
            IOException failure = new("PCM destination rejected a pending write.");
            async ValueTask WriteAsync(ReadOnlyMemory<byte> memory, CancellationToken token)
            {
                Require(!memory.IsEmpty);
                entered.SetResult();
                await release.Task.WaitAsync(token);
                throw failure;
            }
            using ObservedWriteStream destination = new(WriteAsync);
            bool littleEndian = BitConverter.IsLittleEndian != swap;
            Task operation = (littleEndian
                ? PcmStreamWriter.WriteLittleEndianAsync(destination, frame, scratch, cancellation.Token)
                : PcmStreamWriter.WriteBigEndianAsync(destination, frame, scratch, cancellation.Token)).AsTask();
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Require(!operation.IsCompleted && source.AllocatedBytes == format.GetBufferSize(10000));
                Require(scratch.AllocatedBytes == (swap ? 64 * 1024 : 0));
            }
            finally
            {
                if (cancel)
                {
                    await cancellation.CancelAsync();
                }
                else
                {
                    release.TrySetResult();
                }
            }
            bool propagated = false;
            try
            {
                await operation;
            }
            catch (IOException error) when (!cancel && ReferenceEquals(error, failure))
            {
                propagated = true;
            }
            catch (OperationCanceledException error) when (cancel && error.CancellationToken == cancellation.Token)
            {
                propagated = true;
            }
            Require(propagated && !destination.WasDisposed && scratch.AllocatedBytes == 0);
            Require(!frame.GetPlane(0).IsEmpty);
            frame.Dispose();
            Require(source.AllocatedBytes == 0);
        }

        private static void Require(bool condition)
        {
            if (!condition)
            {
                throw new InvalidDataException("PCM stream output changed bits, direct storage, chunk bounds or borrowed ownership.");
            }
        }
    }
}
