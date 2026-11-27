// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Codecs.Transforms;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Formats.Video;

namespace Mediana.Codecs.Mpeg4Visual
{
    internal partial class MicrosoftMpeg4PictureDecoder
    {
        private const int PredictionSize = 16;
        private const int DcBase = 1024;
        private readonly MediaBufferAllocator allocator;
        private readonly bool wmv2;
        private readonly int width;
        private readonly int height;
        private readonly int mbWidth;
        private readonly int mbHeight;
        private readonly int stride;
        private readonly int lumaLength;
        private readonly int pictureLength;
        private readonly int wmv2SliceHeight;
        private readonly bool wmv2MotionFilter;
        private readonly bool wmv2LoopFilter;
        private readonly bool wmv2AdaptiveTransform;
        private readonly bool wmv2IntraX8;
        private readonly bool wmv2DirectionalMotion;
        private readonly bool wmv2MacroblockTable;
        private MediaBuffer? reference;
        private WritableMediaBuffer? predictionsBuffer;
        private int sliceHeight;
        private int quantizer;
        private int lumaTable;
        private int chromaTable;
        private int dcTable;
        private int escapeRunBits;
        private int escapeLevelBits;
        private bool macroblockTable;
        private bool noRounding;
        private bool alternateRounding;

        internal MicrosoftMpeg4PictureDecoder(MicrosoftMpeg4Configuration configuration, MediaBufferAllocator allocator, bool wmv2)
        {
            this.allocator = allocator;
            this.wmv2 = wmv2;
            width = configuration.Width;
            height = configuration.Height;
            mbWidth = checked((width + 15) / 16);
            mbHeight = checked((height + 15) / 16);
            stride = checked(mbWidth * 16);
            lumaLength = checked(stride * mbHeight * 16);
            pictureLength = checked(lumaLength + lumaLength / 2);
            Planes =
            [
                new(0, stride, width, height),
                new(lumaLength, stride / 2, (width + 1) / 2, (height + 1) / 2),
                new(lumaLength + lumaLength / 4, stride / 2, (width + 1) / 2, (height + 1) / 2)
            ];
            if (wmv2)
            {
                if (configuration.CodecPrivateData.Length < 4)
                {
                    throw new InvalidDataException("WMV2 requires a four-byte codec header.");
                }
                Mpeg4VisualBitReader header = new(configuration.CodecPrivateData.Span);
                header.Skip(16);
                wmv2MotionFilter = header.Flag();
                wmv2LoopFilter = header.Flag();
                wmv2AdaptiveTransform = header.Flag();
                wmv2IntraX8 = header.Flag();
                wmv2DirectionalMotion = header.Flag();
                wmv2MacroblockTable = header.Flag();
                wmv2SliceHeight = GetSliceHeight(header.Read(3));
            }
        }

        internal PlaneLayout[] Planes { get; }

        internal static bool ReadIntraFlag(ref Mpeg4VisualBitReader bits, bool wmv2)
        {
            int type = bits.Read(wmv2 ? 1 : 2);
            return type switch
            {
                0 => true,
                1 => false,
                _ => throw new InvalidDataException("Invalid Microsoft MPEG-4 picture type.")
            };
        }

        internal MediaBuffer Decode(ReadOnlySpan<byte> data, CancellationToken cancellationToken)
        {
            Mpeg4VisualBitReader bits = new(data);
            bool intra = ReadIntraFlag(ref bits, wmv2);
            if (wmv2 && intra)
            {
                bits.Skip(7);
            }
            quantizer = bits.Read(5);
            if (quantizer == 0)
            {
                throw new InvalidDataException("Invalid Microsoft MPEG-4 quantizer.");
            }
            escapeRunBits = escapeLevelBits = 0;

            if (intra)
            {
                ReadIntraHeader(ref bits);
            }
            else if (reference is null)
            {
                throw new InvalidDataException("Microsoft MPEG-4 decoding requires an opening intra picture.");
            }
            else if (wmv2 && IsWmv2RepeatPicture(bits))
            {
                return reference.Retain();
            }

            using WritableMediaBuffer writable = allocator.Allocate(pictureLength);
            predictionsBuffer ??= allocator.Allocate(checked(mbWidth * 10 * PredictionSize * sizeof(int)));
            Span<int> predictions = MemoryMarshal.Cast<byte, int>(predictionsBuffer.Span);
            predictions.Clear();
            if (intra)
            {
                DecodeIntra(ref bits, writable.Span, predictions, cancellationToken);
                if (!wmv2)
                {
                    if (bits.RemainingBits is >= 17 and < 25)
                    {
                        bits.Skip(16);
                        alternateRounding = bits.Flag();
                    }
                    else if (bits.RemainingBits < 17)
                    {
                        alternateRounding = false;
                    }
                }
            }
            else
            {
                DecodePredicted(ref bits, writable.Span, predictions, reference!.Memory.Span, cancellationToken);
            }
            MediaBuffer decoded = writable.Publish();
            reference?.Dispose();
            reference = decoded;
            return decoded.Retain();
        }

        internal void Reset()
        {
            reference?.Dispose();
            reference = null;
            predictionsBuffer?.Dispose();
            predictionsBuffer = null;
            sliceHeight = 0;
            noRounding = alternateRounding = false;
        }

        private void ReadIntraHeader(ref Mpeg4VisualBitReader bits)
        {
            if (wmv2)
            {
                sliceHeight = wmv2SliceHeight;
                if (wmv2IntraX8 && bits.Flag())
                {
                    throw new NotSupportedException("WMV2 IntraX8 pictures are not supported.");
                }
                macroblockTable = wmv2MacroblockTable && bits.Flag();
            }
            else
            {
                sliceHeight = GetSliceHeight(bits.Read(5) - 22);
                macroblockTable = false;
            }
            if (!macroblockTable)
            {
                chromaTable = ReadTable(ref bits);
                lumaTable = ReadTable(ref bits);
            }
            dcTable = bits.Read(1);
            noRounding = true;
        }

        private int GetSliceHeight(int slices)
        {
            if (slices <= 0 || slices > mbHeight)
            {
                throw new InvalidDataException("Invalid Microsoft MPEG-4 slice count.");
            }
            return mbHeight / slices;
        }

        private static int ReadTable(ref Mpeg4VisualBitReader bits) => bits.Flag() ? 1 + bits.Read(1) : 0;

        private int PredictionOffset(int plane, int x, int y)
        {
            if (x < 0 || y < 0)
            {
                return -1;
            }
            int block = plane == 0
                ? (y % 3) * (mbWidth * 2) + x
                : (6 + (plane - 1) * 2 + y % 2) * mbWidth + x;
            return block * PredictionSize;
        }

        private Span<byte> GetBlock(Span<byte> pixels, int plane, int x, int y, out int blockStride)
        {
            blockStride = plane == 0 ? stride : stride / 2;
            int start = plane == 0 ? 0 : lumaLength + (plane - 1) * (lumaLength / 4);
            return pixels[(start + y * 8 * blockStride + x * 8)..];
        }

        private static void GetBlockPosition(int mbX, int mbY, int block, out int plane, out int x, out int y)
        {
            plane = block < 4 ? 0 : block - 3;
            x = plane == 0 ? mbX * 2 + (block & 1) : mbX;
            y = plane == 0 ? mbY * 2 + (block >> 1) : mbY;
        }

        private void ReadCoefficients(ref Mpeg4VisualBitReader bits, scoped Span<int> coefficients, int table,
            bool intra, scoped ReadOnlySpan<byte> scan)
        {
            MicrosoftMpeg4RunLevel codebook = MicrosoftMpeg4Codebooks.Residual[table];
            int position = intra ? 1 : 0;
            while (true)
            {
                int level = codebook.Read(ref bits, wmv2, intra, quantizer, ref escapeRunBits, ref escapeLevelBits, out int run, out bool last);
                position += run;
                if (position >= scan.Length || level == 0)
                {
                    throw new InvalidDataException("Invalid Microsoft MPEG-4 coefficient run or level.");
                }
                coefficients[scan[position++]] = level;
                if (last)
                {
                    return;
                }
            }
        }

        private void WriteBlock(Span<int> coefficients, Span<byte> pixels, int plane, int x, int y, bool intra)
        {
            Dequantize(coefficients, intra ? 1 : 0);
            Span<byte> target = GetBlock(pixels, plane, x, y, out int blockStride);
            if (wmv2)
            {
                Wmv2InverseDct.Write(coefficients, target, blockStride, intra);
            }
            else
            {
                BlockInverseDct8x8.Write(coefficients, target, blockStride, intra);
            }
        }

        private void Dequantize(Span<int> coefficients, int start)
        {
            int multiplier = 2 * quantizer;
            int bias = (quantizer - 1) | 1;
            for (int i = start; i < coefficients.Length; i++)
            {
                int level = coefficients[i];
                coefficients[i] = level * multiplier + Math.Sign(level) * bias;
            }
        }
    }
}
