// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.IO;
using System.Runtime.InteropServices;

namespace Mediana.Tests
{
    public class PcmStreamWriterTests
    {
        [Theory]
        [InlineData(AudioSampleFormat.Unsigned8)]
        [InlineData(AudioSampleFormat.Signed16)]
        [InlineData(AudioSampleFormat.Signed32)]
        [InlineData(AudioSampleFormat.Float32)]
        [InlineData(AudioSampleFormat.Float64)]
        public async Task ExplicitByteOrderPreservesBitsAndWritesDirectlyOrWithBoundedScratch(AudioSampleFormat format)
        {
            await PcmStreamOutputTestInput.VerifyAsync(format, true);
            await PcmStreamOutputTestInput.VerifyAsync(format, false);
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public async Task PendingWriteKeepsStorageUntilCancellationOrFailure(bool swap, bool cancel)
        {
            await PcmStreamOutputTestInput.VerifyPendingFailureAsync(swap, cancel);
        }

        [Fact]
        public async Task InvalidInputCannotWriteAndDisposedFramesAreRejected()
        {
            MediaBufferAllocator allocator = new(8192);
            AudioFormat planar = new(48000, AudioSampleFormat.Signed16, AudioStorageLayout.Planar, "left", "right");
            using AudioFrame frame = AudioSamplePreparationTestInput.CreateFrame(planar, 1024, allocator);
            using MemoryStream destination = new();
            await Assert.ThrowsAsync<NotSupportedException>(async () => await PcmStreamWriter.WriteLittleEndianAsync(destination, frame, allocator));
            Assert.Equal(0, destination.Length);
            AudioFormat interleaved = new(48000, AudioSampleFormat.Float32, AudioStorageLayout.Interleaved, "mono");
            using AudioFrame valid = AudioSamplePreparationTestInput.CreateFrame(interleaved, 1024, allocator);
            using MemoryStream readOnly = new([], writable: false);
            await Assert.ThrowsAsync<ArgumentException>(async () => await PcmStreamWriter.WriteBigEndianAsync(readOnly, valid, allocator));
            using CancellationTokenSource canceled = new();
            await canceled.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await PcmStreamWriter.WriteLittleEndianAsync(destination, valid, allocator, canceled.Token));
            Assert.Equal(0, destination.Length);
            valid.Dispose();
            await Assert.ThrowsAsync<ObjectDisposedException>(async () => await PcmStreamWriter.WriteLittleEndianAsync(destination, valid, allocator));
            Assert.Equal(0, destination.Length);
            frame.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task InsufficientSwapBudgetCannotWriteOrTakeOwnershipOfInput()
        {
            AudioFormat format = new(48000, AudioSampleFormat.Signed16, AudioStorageLayout.Interleaved, "mono");
            MediaBufferAllocator source = new(format.GetBufferSize(32769));
            MediaBufferAllocator insufficient = new(65535);
            using AudioFrame frame = AudioSamplePreparationTestInput.CreateFrame(format, 32769, source);
            using MemoryStream destination = new();
            await Assert.ThrowsAsync<MediaMemoryLimitException>(async () =>
            {
                if (BitConverter.IsLittleEndian)
                {
                    await PcmStreamWriter.WriteBigEndianAsync(destination, frame, insufficient);
                }
                else
                {
                    await PcmStreamWriter.WriteLittleEndianAsync(destination, frame, insufficient);
                }
            });
            Assert.Equal(0, destination.Length);
            Assert.Equal(0, insufficient.AllocatedBytes);
            Assert.Equal(format.GetBufferSize(32769), source.AllocatedBytes);
            Assert.NotEmpty(frame.GetPlane(0).ToArray());
        }

        [Fact]
        public async Task FloatingPointPayloadsAreSerializedWithoutNumericalConversion()
        {
            AudioFormat format = new(48000, AudioSampleFormat.Float32, AudioStorageLayout.Interleaved, "mono");
            MediaBufferAllocator allocator = new(1024);
            using WritableMediaBuffer writable = allocator.Allocate(format.GetBufferSize(4));
            uint[] bits = [0x80000000U, 0x7f800000U, 0xff800000U, 0x7fc01234U];
            bits.AsSpan().CopyTo(MemoryMarshal.Cast<byte, uint>(writable.Memory.Span));
            using MediaBuffer buffer = writable.Publish();
            using AudioFrame frame = new(format, bits.Length, buffer);
            using MemoryStream little = new();
            using MemoryStream big = new();
            await PcmStreamWriter.WriteLittleEndianAsync(little, frame, allocator);
            await PcmStreamWriter.WriteBigEndianAsync(big, frame, allocator);
            Assert.Equal(new byte[] { 0, 0, 0, 128, 0, 0, 128, 127, 0, 0, 128, 255, 52, 18, 192, 127 }, little.ToArray());
            Assert.Equal(new byte[] { 128, 0, 0, 0, 127, 128, 0, 0, 255, 128, 0, 0, 127, 192, 18, 52 }, big.ToArray());
            Assert.Equal(bits, MemoryMarshal.Cast<byte, uint>(frame.GetPlane(0).Span).ToArray());
        }
    }
}
