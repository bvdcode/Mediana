// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Flac;
using Mediana.Core.Audio;

namespace Mediana.Tests
{
    public class FlacSubframeTests
    {
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        public void EveryFixedPredictorRestoresWarmupsAndPartitionedEscapedZeroResiduals(int order)
        {
            FlacFrameTestWriter writer = new();
            writer.Write((8 + order) << 1, 8);
            for (int i = 0; i < order; i++)
            {
                writer.Write(order == 0 ? 0 : i + 1, 16);
            }
            writer.Write(0, 2);
            writer.Write(1, 4);
            for (int partition = 0; partition < 2; partition++)
            {
                writer.Write(15, 4);
                writer.Write(0, 5);
            }
            int[] expected = Enumerable.Range(0, 16).Select(i => order switch
            {
                0 => 0,
                1 => 1 << 16,
                >= 2 and <= 4 => (i + 1) << 16,
                _ => throw new InvalidOperationException()
            }).ToArray();
            Assert.Equal(expected, DecodeSubframe(writer.Finish(), 16));
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(0, 3)]
        [InlineData(1, 17)]
        [InlineData(1, 30)]
        public void RiceParametersIncludingSplitUnaryAndRemainderDecodeSignedResiduals(int method, int parameter)
        {
            long[] residuals = [0, -1, 1, -17, 42, -63, 127, -255, 9, 5, 3, 2, 1, 0, 0, 0];
            FlacFrameTestWriter writer = new();
            writer.Write(16, 8);
            writer.Write(method, 2);
            writer.Write(0, 4);
            writer.Write(parameter, method switch { 0 => 4, 1 => 5, _ => throw new InvalidOperationException() });
            foreach (long residual in residuals)
            {
                writer.Rice(residual, parameter);
            }
            Assert.Equal(residuals.Select(x => (int)x << 16).ToArray(), DecodeSubframe(writer.Finish(), 16));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(16)]
        [InlineData(32)]
        public void LinearPredictionUsesReverseCoefficientOrderWideSumsAndArithmeticShift(int order)
        {
            const int count = 64;
            FlacFrameTestWriter writer = new(count);
            writer.Write((order + 31) << 1, 8);
            for (int i = 0; i < order; i++)
            {
                writer.Write(-1000 + i * 2, 24);
            }
            writer.Write(14, 4);
            writer.Write(13, 5);
            for (int i = 0; i < order; i++)
            {
                writer.Write(i switch { 0 => 8192, _ => 0 }, 15);
            }
            writer.Write(1, 2);
            writer.Write(0, 4);
            writer.Write(31, 5);
            writer.Write(4, 5);
            for (int i = order; i < count; i++)
            {
                writer.Write(-1, 4);
            }
            int[] expected = Enumerable.Range(0, count).Select(i =>
                (i < order ? -1000 + i * 2 : -1000 + (order - 1) * 2 - (i - order + 1)) << 8).ToArray();
            Assert.Equal(expected, DecodeSubframe(writer.Finish(), 24));
        }

        [Fact]
        public void LinearPredictionAccumulatesMoreThan32BitsWithoutLosingPrecision()
        {
            FlacFrameTestWriter writer = new(33);
            writer.Write(126, 8);
            for (int i = 0; i < 32; i++)
            {
                writer.Write(int.MaxValue, 32);
            }
            writer.Write(14, 4);
            writer.Write(15, 5);
            for (int i = 0; i < 32; i++)
            {
                writer.Write(i % 2 == 0 ? 16383 : -16384, 15);
            }
            writer.Write(0, 2);
            writer.Write(0, 4);
            writer.Write(15, 4);
            writer.Write(0, 5);
            int[] expected = [.. Enumerable.Repeat(int.MaxValue, 32), -1048576];
            Assert.Equal(expected, DecodeSubframe(writer.Finish(), 32));
        }

        [Fact]
        public void ReservedSyntaxInvalidPartitionsAndPredictionOverflowAreRejected()
        {
            List<FlacFrameTestWriter> invalid = [];
            foreach (int code in new[] { 4, 128, 62 })
            {
                FlacFrameTestWriter writer = new();
                writer.Write(code, 8);
                invalid.Add(writer);
            }
            FlacFrameTestWriter wasted = new();
            wasted.Write(1, 8);
            wasted.Unary(31);
            invalid.Add(wasted);
            FlacFrameTestWriter precision = new();
            precision.Write(64, 8);
            precision.Write(0, 32);
            precision.Write(15, 4);
            precision.Write(0, 5);
            invalid.Add(precision);
            FlacFrameTestWriter negativeShift = new();
            negativeShift.Write(64, 8);
            negativeShift.Write(0, 32);
            negativeShift.Write(4, 4);
            negativeShift.Write(-1, 5);
            invalid.Add(negativeShift);
            FlacFrameTestWriter partition = new(17);
            partition.Write(16, 8);
            partition.Write(0, 2);
            partition.Write(1, 4);
            invalid.Add(partition);
            FlacFrameTestWriter residual = new();
            residual.Write(16, 8);
            residual.Write(1, 2);
            residual.Write(0, 4);
            residual.Write(30, 5);
            residual.Rice(int.MinValue, 30);
            invalid.Add(residual);
            FlacFrameTestWriter overflow = new();
            overflow.Write(18, 8);
            overflow.Write(int.MaxValue, 32);
            overflow.Write(0, 2);
            overflow.Write(0, 4);
            overflow.Write(0, 4);
            overflow.Rice(1, 0);
            invalid.Add(overflow);
            foreach (FlacFrameTestWriter writer in invalid)
            {
                Assert.Throws<InvalidDataException>(() => DecodeSubframe(writer.Finish(), 32));
            }
        }

        private static int[] DecodeSubframe(byte[] data, int bits)
        {
            FlacConfiguration configuration = FlacConfiguration.ReadStreamInfo(
                FlacTestInput.StreamInfo(maximum: 64, bits: bits, channels: 1));
            Assert.True(FlacFrameHeader.TryRead(data, configuration, out FlacFrameHeader header));
            FlacSubframeDecoder decoder = new();
            long[] values = new long[header.BlockSize];
            int position = header.ByteLength * 8;
            for (int available = header.ByteLength; available <= data.Length; available++)
            {
                FlacBitReader reader = new(data.AsSpan(0, available), position);
                bool ready = decoder.TryRead(ref reader, values, bits, CancellationToken.None);
                position = reader.Position;
                if (ready)
                {
                    return values.Select(value => (int)(value << (32 - bits))).ToArray();
                }
            }
            throw new InvalidDataException("The FLAC subframe is truncated.");
        }
    }
}
