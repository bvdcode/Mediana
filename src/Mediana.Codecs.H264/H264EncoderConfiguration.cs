// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Codecs.H264
{
    internal static class H264EncoderConfiguration
    {
        private const int Level = 41;
        private const int MaximumFrameMacroblocks = 8192;
        private const int MaximumDimensionMacroblocks = 256;
        internal const int MaximumMacroblocksPerSecond = 245760;
        internal const int MaximumBitsPerSecond = 50000000;
        internal const int MaximumFramesPerSecond = 172;
        internal const int MinimumCompressionRatio = 2;

        public static byte[] Create(VideoFormat format, MediaBufferAllocator allocator, int qp)
        {
            if (format.PixelFormat != VideoPixelFormat.Yuv420P8 || (format.Width & 1) != 0 || (format.Height & 1) != 0)
            {
                throw new NotSupportedException("H.264 encoding requires even-sized 8-bit planar YUV 4:2:0.");
            }
            int width = checked((int)(((long)format.Width + 15) / 16));
            int height = checked((int)(((long)format.Height + 15) / 16));
            if ((long)width * height > MaximumFrameMacroblocks ||
                width > MaximumDimensionMacroblocks || height > MaximumDimensionMacroblocks)
            {
                throw new NotSupportedException("The H.264 picture exceeds Baseline level 4.1 dimensions.");
            }
            using H264BitWriter sequence = new(allocator, 4096, 0x67);
            sequence.WriteBits(66, 8);
            sequence.WriteBits(0xC0, 8);
            sequence.WriteBits(Level, 8);
            sequence.WriteUnsigned(0);
            sequence.WriteUnsigned(0);
            sequence.WriteUnsigned(2);
            sequence.WriteUnsigned(0);
            sequence.WriteFlag(false);
            sequence.WriteUnsigned((uint)(width - 1));
            sequence.WriteUnsigned((uint)(height - 1));
            sequence.WriteFlag(true);
            sequence.WriteFlag(true);
            bool crop = width * 16 != format.Width || height * 16 != format.Height;
            sequence.WriteFlag(crop);
            if (crop)
            {
                sequence.WriteUnsigned(0);
                sequence.WriteUnsigned((uint)(width * 16 - format.Width) / 2);
                sequence.WriteUnsigned(0);
                sequence.WriteUnsigned((uint)(height * 16 - format.Height) / 2);
            }
            sequence.WriteFlag(true);
            WriteVui(sequence, format);
            using MediaBuffer sps = sequence.Finish();
            using H264BitWriter picture = new(allocator, 4096, 0x68);
            picture.WriteUnsigned(0);
            picture.WriteUnsigned(0);
            picture.WriteFlag(false);
            picture.WriteFlag(false);
            picture.WriteUnsigned(0);
            picture.WriteUnsigned(0);
            picture.WriteUnsigned(0);
            picture.WriteFlag(false);
            picture.WriteBits(0, 2);
            picture.WriteSigned(qp - 26);
            picture.WriteSigned(0);
            picture.WriteSigned(0);
            picture.WriteFlag(true);
            picture.WriteFlag(false);
            picture.WriteFlag(false);
            using MediaBuffer pps = picture.Finish();
            byte[] configuration = new byte[11 + sps.Memory.Length + pps.Memory.Length];
            configuration[0] = 1;
            sps.Memory.Span.Slice(1, 3).CopyTo(configuration.AsSpan(1));
            configuration[4] = 0xFF;
            configuration[5] = 0xE1;
            BinaryPrimitives.WriteUInt16BigEndian(configuration.AsSpan(6), (ushort)sps.Memory.Length);
            sps.Memory.Span.CopyTo(configuration.AsSpan(8));
            int offset = 8 + sps.Memory.Length;
            configuration[offset++] = 1;
            BinaryPrimitives.WriteUInt16BigEndian(configuration.AsSpan(offset), (ushort)pps.Memory.Length);
            pps.Memory.Span.CopyTo(configuration.AsSpan(offset + 2));
            return configuration;
        }

        public static bool ExceedsAccessUnitLimit(int bytes, int macroblocks)
        {
            long pictureLimit = Math.Max((long)macroblocks * MaximumFramesPerSecond, MaximumMacroblocksPerSecond);
            return (long)bytes * MaximumFramesPerSecond * MinimumCompressionRatio > 384 * pictureLimit;
        }

        private static void WriteVui(H264BitWriter bits, VideoFormat format)
        {
            bits.WriteFlag(format.SampleAspectRatio is not null);
            if (format.SampleAspectRatio is SampleAspectRatio aspect)
            {
                if (aspect.Numerator > ushort.MaxValue || aspect.Denominator > ushort.MaxValue)
                {
                    throw new NotSupportedException("The H.264 sample aspect ratio exceeds its 16-bit fields.");
                }
                bits.WriteBits(255, 8);
                bits.WriteBits((uint)aspect.Numerator, 16);
                bits.WriteBits((uint)aspect.Denominator, 16);
            }
            bits.WriteFlag(false);
            bits.WriteFlag(true);
            bits.WriteBits(5, 3);
            bits.WriteFlag(format.Color.Range switch
            {
                ColorRange.Full => true,
                ColorRange.Limited => false,
                ColorRange.Unspecified => throw new NotSupportedException("H.264 encoding requires an explicit color range."),
                _ => throw new ArgumentOutOfRangeException(nameof(format))
            });
            bits.WriteFlag(true);
            bits.WriteBits((uint)Primaries(format.Color.Primaries), 8);
            bits.WriteBits((uint)Transfer(format.Color.Transfer), 8);
            bits.WriteBits((uint)Matrix(format.Color.Matrix), 8);
            bits.WriteFlag(true);
            int location = format.Color.ChromaLocation switch
            {
                ChromaLocation.Left => 0,
                ChromaLocation.Center => 1,
                ChromaLocation.TopLeft => 2,
                ChromaLocation.Top => 3,
                ChromaLocation.BottomLeft => 4,
                ChromaLocation.Bottom => 5,
                ChromaLocation.Unspecified => throw new NotSupportedException("H.264 encoding requires an explicit chroma location."),
                _ => throw new ArgumentOutOfRangeException(nameof(format))
            };
            bits.WriteUnsigned((uint)location);
            bits.WriteUnsigned((uint)location);
            bits.WriteFlag(false);
            bits.WriteFlag(false);
            bits.WriteFlag(false);
            bits.WriteFlag(false);
            bits.WriteFlag(true);
            bits.WriteFlag(true);
            bits.WriteUnsigned(0);
            bits.WriteUnsigned(0);
            bits.WriteUnsigned(0);
            bits.WriteUnsigned(0);
            bits.WriteUnsigned(0);
            bits.WriteUnsigned(0);
        }

        private static int Primaries(ColorPrimaries value) => value switch
        {
            ColorPrimaries.Unspecified => 2,
            ColorPrimaries.Bt709 => 1,
            ColorPrimaries.Bt470M => 4,
            ColorPrimaries.Bt470Bg => 5,
            ColorPrimaries.Smpte170M => 6,
            ColorPrimaries.Film => 8,
            ColorPrimaries.Bt2020 => 9,
            ColorPrimaries.Xyz => 10,
            ColorPrimaries.DciP3 => 11,
            ColorPrimaries.DisplayP3 => 12,
            ColorPrimaries.Ebu3213 => 22,
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        };

        private static int Transfer(TransferFunction value) => value switch
        {
            TransferFunction.Unspecified => 2,
            TransferFunction.Bt709 => 1,
            TransferFunction.Gamma22 => 4,
            TransferFunction.Gamma28 => 5,
            TransferFunction.Smpte240M => 7,
            TransferFunction.Linear => 8,
            TransferFunction.Log100 => 9,
            TransferFunction.Log316 => 10,
            TransferFunction.Iec61966_2_4 => 11,
            TransferFunction.Bt1361Extended => 12,
            TransferFunction.Srgb => 13,
            TransferFunction.Pq => 16,
            TransferFunction.Smpte428 => 17,
            TransferFunction.Hlg => 18,
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        };

        private static int Matrix(MatrixCoefficients value) => value switch
        {
            MatrixCoefficients.Unspecified => 2,
            MatrixCoefficients.Rgb => 0,
            MatrixCoefficients.Bt709 => 1,
            MatrixCoefficients.Fcc => 4,
            MatrixCoefficients.Bt601 => 6,
            MatrixCoefficients.Smpte240M => 7,
            MatrixCoefficients.YCgCo => 8,
            MatrixCoefficients.Bt2020NonConstant => 9,
            MatrixCoefficients.Bt2020Constant => 10,
            MatrixCoefficients.Smpte2085 => 11,
            MatrixCoefficients.ChromaticityNonConstant => 12,
            MatrixCoefficients.ChromaticityConstant => 13,
            MatrixCoefficients.ICtCp => 14,
            MatrixCoefficients.IptC2 => 15,
            MatrixCoefficients.YCgCoRe => 16,
            MatrixCoefficients.YCgCoRo => 17,
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        };
    }
}
