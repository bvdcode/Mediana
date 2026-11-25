// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core.Memory;
using Mediana.Formats.Video;

namespace Mediana.Codecs.Mpeg4Visual
{
    internal partial class MicrosoftMpeg4PictureDecoder
    {
        private static ReadOnlySpan<byte> Wmv2MacroblockOrder => [0, 2, 1, 1, 0, 2, 2, 1, 0];
        private const int Version3MacroblockTable = 3;

        private void DecodePredicted(ref Mpeg4VisualBitReader bits, scoped Span<byte> pixels, scoped Span<int> predictions,
            scoped ReadOnlySpan<byte> previous, CancellationToken cancellationToken)
        {
            using WritableMediaBuffer motionStorage = allocator.Allocate(checked(mbWidth * 4 * sizeof(int)));
            Span<int> motion = MemoryMarshal.Cast<byte, int>(motionStorage.Span);
            using WritableMediaBuffer? skipStorage = wmv2 ? allocator.Allocate(checked(mbWidth * mbHeight)) : null;
            Span<byte> skipped = skipStorage is null ? default : skipStorage.Span;
            int mbTable = Version3MacroblockTable;
            bool inlineSkip = false;
            bool modifiedMotion = false;
            bool perMacroblockTransform = false;
            int pictureTransform = 0;
            if (wmv2)
            {
                ReadWmv2SkipMap(ref bits, skipped);
                int quantizerBand = (quantizer - 1) / 10;
                mbTable = Wmv2MacroblockOrder[Math.Min(quantizerBand, 2) * 3 + ReadTable(ref bits)];
                modifiedMotion = wmv2MotionFilter && bits.Flag();
                if (wmv2AdaptiveTransform)
                {
                    perMacroblockTransform = !bits.Flag();
                    if (!perMacroblockTransform)
                    {
                        pictureTransform = ReadTable(ref bits);
                    }
                }
                macroblockTable = wmv2MacroblockTable && bits.Flag();
                noRounding = !noRounding;
            }
            else
            {
                inlineSkip = bits.Flag();
                macroblockTable = false;
                noRounding = alternateRounding && !noRounding;
            }

            if (!macroblockTable)
            {
                lumaTable = chromaTable = ReadTable(ref bits);
            }
            dcTable = bits.Read(1);
            int motionTable = bits.Read(1);
            MicrosoftMpeg4Vlc macroblockCodes = MicrosoftMpeg4InterCodebooks.Macroblock[mbTable];
            for (int mbY = 0; mbY < mbHeight; mbY++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                bool sliceStart = mbY % sliceHeight == 0;
                for (int mbX = 0; mbX < mbWidth; mbX++)
                {
                    int motionOffset = MotionOffset(mbX, mbY);
                    motion[motionOffset] = motion[motionOffset + 1] = 0;
                    bool skip = wmv2 ? skipped[mbY * mbWidth + mbX] != 0 : inlineSkip && bits.Flag();
                    if (skip)
                    {
                        ClearIntraPredictions(predictions, mbX, mbY);
                        PredictMacroblock(pixels, previous, mbX, mbY, 0, 0, modifiedMotion, false);
                        if (wmv2LoopFilter)
                        {
                            FilterMacroblock(pixels, mbX, mbY, skipped);
                        }
                        continue;
                    }
                    int code = macroblockCodes.Read(ref bits);
                    int pattern = code & 63;
                    if ((code & 64) == 0)
                    {
                        DecodeIntraMacroblock(ref bits, pixels, predictions, mbX, mbY, sliceStart, pattern, predictPattern: false);
                        if (wmv2LoopFilter)
                        {
                            FilterMacroblock(pixels, mbX, mbY, skipped);
                        }
                        continue;
                    }

                    ClearIntraPredictions(predictions, mbX, mbY);
                    PredictMotion(ref bits, motion, mbX, mbY, sliceStart, modifiedMotion, out int motionX, out int motionY);
                    if (pattern != 0 && macroblockTable)
                    {
                        lumaTable = chromaTable = ReadTable(ref bits);
                    }
                    int transform = pictureTransform;
                    bool perBlockTransform = false;
                    if (pattern != 0 && perMacroblockTransform)
                    {
                        perBlockTransform = bits.Flag();
                        if (!perBlockTransform)
                        {
                            transform = ReadTable(ref bits);
                        }
                    }
                    ReadMotion(ref bits, motionTable, ref motionX, ref motionY);
                    bool quarterStep = modifiedMotion && ((motionX | motionY) & 1) != 0 && bits.Flag();
                    motion[motionOffset] = motionX;
                    motion[motionOffset + 1] = motionY;
                    PredictMacroblock(pixels, previous, mbX, mbY, motionX, motionY, modifiedMotion, quarterStep);
                    if (pattern != 0)
                    {
                        DecodeInterResidual(ref bits, pixels, mbX, mbY, pattern, transform, perBlockTransform);
                    }
                    if (wmv2LoopFilter)
                    {
                        FilterMacroblock(pixels, mbX, mbY, skipped);
                    }
                }
            }
        }

        private bool IsWmv2RepeatPicture(Mpeg4VisualBitReader bits)
        {
            int mode = bits.Read(2);
            int groups = mode switch
            {
                2 => mbHeight,
                3 => mbWidth,
                _ => 0
            };
            if (groups == 0)
            {
                return false;
            }
            while (groups > 0)
            {
                int count = Math.Min(24, groups);
                if (bits.Read(count) != (1 << count) - 1)
                {
                    return false;
                }
                groups -= count;
            }
            return true;
        }

        private void ReadWmv2SkipMap(ref Mpeg4VisualBitReader bits, scoped Span<byte> skipped)
        {
            int mode = bits.Read(2);
            if (mode == 0)
            {
                skipped.Clear();
                return;
            }
            if (mode == 1)
            {
                for (int i = 0; i < skipped.Length; i++)
                {
                    skipped[i] = (byte)bits.Read(1);
                }
                return;
            }
            bool rows = mode == 2;
            int groups = rows ? mbHeight : mbWidth;
            int groupSize = rows ? mbWidth : mbHeight;
            for (int group = 0; group < groups; group++)
            {
                bool fullGroup = bits.Flag();
                for (int i = 0; i < groupSize; i++)
                {
                    int index = rows ? group * mbWidth + i : i * mbWidth + group;
                    skipped[index] = fullGroup ? (byte)1 : (byte)bits.Read(1);
                }
            }
        }

        private void ClearIntraPredictions(Span<int> predictions, int mbX, int mbY)
        {
            for (int block = 0; block < 6; block++)
            {
                GetBlockPosition(mbX, mbY, block, out int plane, out int x, out int y);
                int offset = PredictionOffset(plane, x, y);
                predictions.Slice(offset, PredictionSize).Clear();
            }
        }

        private void PredictMotion(ref Mpeg4VisualBitReader bits, scoped ReadOnlySpan<int> motion,
            int x, int y, bool sliceStart, bool modifiedMotion, out int motionX, out int motionY)
        {
            int left = x == 0 ? -1 : MotionOffset(x - 1, y);
            if (sliceStart)
            {
                motionX = ReadPrediction(motion, left, 0);
                motionY = ReadPrediction(motion, left, 1);
                return;
            }
            int above = MotionOffset(x, y - 1);
            int diagonal = x + 1 == mbWidth ? -1 : MotionOffset(x + 1, y - 1);
            int ax = ReadPrediction(motion, left, 0);
            int ay = ReadPrediction(motion, left, 1);
            int bx = motion[above];
            int by = motion[above + 1];
            if (wmv2DirectionalMotion && !modifiedMotion && x != 0 && Math.Max(Math.Abs(ax - bx), Math.Abs(ay - by)) >= 8)
            {
                bool useAbove = bits.Flag();
                motionX = useAbove ? bx : ax;
                motionY = useAbove ? by : ay;
                return;
            }
            motionX = Median(ax, bx, ReadPrediction(motion, diagonal, 0));
            motionY = Median(ay, by, ReadPrediction(motion, diagonal, 1));
        }

        private int MotionOffset(int x, int y) => 2 * ((y & 1) * mbWidth + x);

        private static int Median(int a, int b, int c) => Math.Max(Math.Min(a, b), Math.Min(Math.Max(a, b), c));

        private static void ReadMotion(ref Mpeg4VisualBitReader bits, int table, ref int x, ref int y)
        {
            int symbol = MicrosoftMpeg4InterCodebooks.Motion[table].Read(ref bits);
            int vector = symbol == MicrosoftMpeg4InterCodebooks.MotionEscape
                ? (bits.Read(6) << 6) | bits.Read(6)
                : MicrosoftMpeg4InterCodebooks.GetMotion(table, symbol);
            x = WrapMotion(x + (vector >> 6) - 32);
            y = WrapMotion(y + (vector & 63) - 32);
        }

        private static int WrapMotion(int value) => value switch
        {
            <= -64 => value + 64,
            >= 64 => value - 64,
            _ => value
        };

        private void PredictMacroblock(Span<byte> pixels, ReadOnlySpan<byte> previous,
            int mbX, int mbY, int motionX, int motionY, bool modifiedMotion, bool quarterStep)
        {
            int paddedHeight = mbHeight * 16;
            if (modifiedMotion && (motionX != 0 || motionY != 0))
            {
                Wmv2MotionCompensation.Predict(pixels, previous[..lumaLength], stride, paddedHeight, width, height,
                    mbX * 16, mbY * 16, motionX, motionY, quarterStep);
            }
            else
            {
                Mpeg4MotionCompensation.PredictHalf(pixels, previous[..lumaLength], stride, paddedHeight,
                    mbX * 16, mbY * 16, 16, 16, motionX, motionY, noRounding, average: false);
            }
            int chromaX = (motionX >> 2) * 2 + ((motionX & 3) != 0 ? 1 : 0);
            int chromaY = (motionY >> 2) * 2 + ((motionY & 3) != 0 ? 1 : 0);
            if (modifiedMotion)
            {
                chromaX = ClipChromaMotion(chromaX, mbX * 8, width / 2);
                chromaY = ClipChromaMotion(chromaY, mbY * 8, height / 2);
            }
            int chromaLength = lumaLength / 4;
            for (int start = lumaLength; start < pictureLength; start += chromaLength)
            {
                Mpeg4MotionCompensation.PredictHalf(pixels[start..], previous[start..], stride / 2, paddedHeight / 2,
                    mbX * 8, mbY * 8, 8, 8, chromaX, chromaY, noRounding, average: false);
            }
        }

        private static int ClipChromaMotion(int motion, int origin, int visibleSize)
        {
            int source = Math.Clamp(origin + (motion >> 1), -8, visibleSize);
            return 2 * (source - origin) + (source == visibleSize ? 0 : motion & 1);
        }

        private void DecodeInterResidual(ref Mpeg4VisualBitReader bits, scoped Span<byte> pixels, int mbX, int mbY,
            int pattern, int transform, bool perBlockTransform)
        {
            Span<int> coefficients = stackalloc int[64];
            ReadOnlySpan<byte> scan = wmv2 ? Wmv2Scan.Inter : Mpeg4Scan.Zigzag;
            for (int block = 0; block < 6; block++)
            {
                if ((pattern & (32 >> block)) == 0)
                {
                    continue;
                }
                int blockTransform = perBlockTransform ? ReadTable(ref bits) : transform;
                GetBlockPosition(mbX, mbY, block, out int plane, out int x, out int y);
                if (blockTransform != 0)
                {
                    Span<byte> target = GetBlock(pixels, plane, x, y, out int blockStride);
                    DecodeAdaptiveResidual(ref bits, coefficients, target, blockStride, splitAcrossRows: blockTransform == 1);
                    continue;
                }
                coefficients.Clear();
                ReadCoefficients(ref bits, coefficients, 3 + lumaTable, intra: false, scan);
                WriteBlock(coefficients, pixels, plane, x, y, intra: false);
            }
        }

        private void DecodeAdaptiveResidual(ref Mpeg4VisualBitReader bits, scoped Span<int> coefficients,
            scoped Span<byte> target, int blockStride, bool splitAcrossRows)
        {
            ReadOnlySpan<byte> scan = splitAcrossRows ? Wmv2Scan.Wide : Wmv2Scan.Tall;
            int selection = ReadTable(ref bits);
            int secondOffset = splitAcrossRows ? 4 * blockStride : 4;
            if (selection != 0)
            {
                coefficients.Clear();
                ReadCoefficients(ref bits, coefficients, 3 + lumaTable, intra: false, scan);
                Dequantize(coefficients, 0);
                Wmv2AdaptiveDct.Add(coefficients, target, blockStride, splitAcrossRows);
            }
            if (selection != 2)
            {
                coefficients.Clear();
                ReadCoefficients(ref bits, coefficients, 3 + lumaTable, intra: false, scan);
                Dequantize(coefficients, 0);
                Wmv2AdaptiveDct.Add(coefficients, target[secondOffset..], blockStride, splitAcrossRows);
            }
        }
    }
}
