// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Codecs;
using Mediana.Codecs.Flac;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using static Mediana.Tests.FlacDecoderTestInput;

namespace Mediana.Tests
{
    public class FlacDecoderTests
    {
        [Theory]
        [InlineData(4, 1)]
        [InlineData(8, 2)]
        [InlineData(12, 3)]
        [InlineData(16, 4)]
        [InlineData(20, 5)]
        [InlineData(24, 6)]
        [InlineData(31, 7)]
        [InlineData(32, 8)]
        public void VerbatimPreservesEveryPrecisionAndAllStandardChannelOrders(int bits, int channels)
        {
            FlacFrameTestWriter writer = new(17, channels - 1);
            long maximum = (1L << (bits - 1)) - 1;
            long[] values = [-maximum - 1, maximum, 0, -1, 1];
            int[] expected = new int[17 * channels];
            for (int c = 0; c < channels; c++)
            {
                writer.Write(2, 8);
                for (int i = 0; i < 17; i++)
                {
                    long sample = values[(i + c) % values.Length];
                    writer.Write(sample, bits);
                    expected[i * channels + c] = (int)(sample << (32 - bits));
                }
            }
            Assert.Equal(expected, Decode(writer.Finish(), bits, channels, 17, packetBytes: 1));
        }

        [Theory]
        [InlineData(8)]
        [InlineData(9)]
        [InlineData(10)]
        public void StereoReconstructionPreserves33BitSideAndOddNegativeSamples(int assignment)
        {
            long[] left = [int.MinValue, int.MaxValue, -1, 0, 1, 37, -17, 63, -3, 9, 3, 4, 5, 6, 7, 8];
            long[] right = [int.MaxValue, int.MinValue, 0, -1, -1, -12, 42, -64, 4, 7, -2, -3, -4, -5, -6, -7];
            FlacFrameTestWriter writer = new(16, assignment, precisionCode: 7);
            for (int c = 0; c < 2; c++)
            {
                writer.Write(2, 8);
                int precision = assignment switch
                {
                    8 or 10 => c switch { 0 => 32, 1 => 33, _ => throw new InvalidOperationException() },
                    9 => c switch { 0 => 33, 1 => 32, _ => throw new InvalidOperationException() },
                    _ => throw new InvalidOperationException()
                };
                for (int i = 0; i < 16; i++)
                {
                    long value = assignment switch
                    {
                        8 => c switch { 0 => left[i], 1 => left[i] - right[i], _ => throw new InvalidOperationException() },
                        9 => c switch { 0 => left[i] - right[i], 1 => right[i], _ => throw new InvalidOperationException() },
                        10 => c switch { 0 => (left[i] + right[i]) >> 1, 1 => left[i] - right[i], _ => throw new InvalidOperationException() },
                        _ => throw new InvalidOperationException()
                    };
                    writer.Write(value, precision);
                }
            }
            int[] expected = Enumerable.Range(0, 16).SelectMany(i => new[] { (int)left[i], (int)right[i] }).ToArray();
            Assert.Equal(expected, Decode(writer.Finish(), 32, 2, 16, packetBytes: 1));
        }

        [Fact]
        public void WastedBitsAreRestoredBeforeMidSideAndConstantSubframes()
        {
            FlacFrameTestWriter writer = new(16, 10);
            writer.Write(1, 8);
            writer.Unary(2);
            writer.Write(-3, 13);
            writer.Write(1, 8);
            writer.Unary(1);
            writer.Write(7, 15);
            Assert.Equal(Enumerable.Range(0, 16).SelectMany(_ => new[] { -10 << 16, -38 << 16 }).ToArray(),
                Decode(writer.Finish(), 16, 2, 16));
        }

        [Fact]
        public void VariableBlocksAndShortFinalBlockKeepContinuousSampleTimestamps()
        {
            byte[] first = Constant(16, 0, true);
            byte[] last = Constant(3, 16, true);
            Assert.Equal(19, Decode([.. first, .. last], 16, 1, 19, variable: true).Length);
            Assert.Throws<InvalidDataException>(() => Decode([.. first, .. last, .. Constant(16, 19, true)], 16, 1, 0, variable: true));
        }

        [Fact]
        public void FixedBlocksKeepFrameNumbersAndUnknownTotalsStillEndExactly()
        {
            byte[] data = [.. Constant(64), .. Constant(64, 1), .. Constant(3, 2)];
            Assert.Equal(131, Decode(data, 16, 1, 131).Length);
            Assert.Equal(131, Decode(data, 16, 1, 0).Length);
        }

        [Fact]
        public void CorruptionTruncationDuplicateFramesAndTotalMismatchAreTerminalAndReleaseStorage()
        {
            byte[] data = Constant();
            byte[] badHeader = (byte[])data.Clone();
            badHeader[7] ^= 1;
            byte[] badBody = (byte[])data.Clone();
            badBody[^3] ^= 1;
            foreach (byte[] invalid in new[] { badHeader, badBody, data[..^1], Array.Empty<byte>(), data.Concat(data).ToArray() })
            {
                Assert.Throws<InvalidDataException>(() => Decode(invalid, 16, 1, 16));
            }
            Assert.Throws<InvalidDataException>(() => Decode(data, 16, 1, 17));
            Assert.Throws<InvalidDataException>(() => Decode(data, 16, 1, 15));
        }

        [Fact]
        public void ResetFailureAndCancellationPreserveCallerOwnedFrames()
        {
            MediaBufferAllocator allocator = new(16384);
            using IMediaCodec<MediaPacket, AudioFrame> decoder = Create(allocator, 16, 1, 16);
            Send(decoder, allocator, Constant());
            using AudioFrame retained = decoder.Receive().Value;
            decoder.Reset();
            Assert.Equal(retained.GetPlane(0).Length, allocator.AllocatedBytes);
            Send(decoder, allocator, Constant());
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();
            Assert.Throws<OperationCanceledException>(() => decoder.Receive(cancellation.Token));
            Assert.Equal(retained.GetPlane(0).Length, allocator.AllocatedBytes);
            Assert.Throws<InvalidOperationException>(() => decoder.Receive());
            decoder.Reset();
            Send(decoder, allocator, Constant());
            decoder.CompleteInput();
            using AudioFrame next = decoder.Receive().Value;
            Assert.True(retained.GetPlane(0).Span.SequenceEqual(next.GetPlane(0).Span));
            Assert.Equal(CodecOutputStatus.EndOfStream, decoder.Receive().Status);
            decoder.Dispose();
            retained.Dispose();
            next.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void BudgetAndFrameLimitFailuresReleaseAllRetainedStorage()
        {
            MediaBufferAllocator allocator = new(128);
            using IMediaCodec<MediaPacket, AudioFrame> decoder = Create(allocator, 16, 1, 16);
            Send(decoder, allocator, Constant());
            Assert.Throws<MediaMemoryLimitException>(() => decoder.Receive());
            Assert.Equal(0, allocator.AllocatedBytes);
            FlacFrameTestWriter writer = new();
            writer.Write(2, 8);
            for (int i = 0; i < 16; i++)
            {
                writer.Write(i, 16);
            }
            MediaBufferAllocator limited = new(4096);
            using IMediaCodec<MediaPacket, AudioFrame> capped = Create(limited, 16, 1, 16, maxFrameBytes: 16);
            Send(capped, limited, writer.Finish());
            Assert.Throws<NotSupportedException>(() => capped.Receive());
            Assert.Equal(0, limited.AllocatedBytes);
        }

    }
}
