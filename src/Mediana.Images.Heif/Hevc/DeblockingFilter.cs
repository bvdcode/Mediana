// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.Runtime.CompilerServices;

namespace Mediana.Images.Heif.Hevc
{
    internal static class DeblockingFilter
    {
        public static void Apply(DecodedPicture picture, IntraSliceHeader slice, CodingBlockMap blocks)
        {
            for (int direction = 0; direction < 2; direction++)
            {
                bool vertical = direction == 0;
                switch (picture.Sequence.BitDepth)
                {
                    case 8:
                        LumaEdges<byte, ByteSampleFormat>(picture.Luma.ByteView, slice, blocks, vertical);

                        if (picture.Cb is not null && picture.Cr is not null)
                        {
                            ChromaEdges<byte, ByteSampleFormat>(picture.Cb.ByteView, slice, blocks, vertical, slice.Picture.CbQpOffset);
                            ChromaEdges<byte, ByteSampleFormat>(picture.Cr.ByteView, slice, blocks, vertical, slice.Picture.CrQpOffset);
                        }
                        break;
                    case 10:
                        LumaEdges<ushort, WordSampleFormat>(picture.Luma.WordView, slice, blocks, vertical);

                        if (picture.Cb is not null && picture.Cr is not null)
                        {
                            ChromaEdges<ushort, WordSampleFormat>(picture.Cb.WordView, slice, blocks, vertical, slice.Picture.CbQpOffset);
                            ChromaEdges<ushort, WordSampleFormat>(picture.Cr.WordView, slice, blocks, vertical, slice.Picture.CrQpOffset);
                        }
                        break;
                    default:
                        throw new InvalidOperationException("Unsupported component sample depth.");
                }
            }
        }

        private static void LumaEdges<TSample, TFormat>(SamplePlaneView<TSample> plane, IntraSliceHeader slice, CodingBlockMap blocks, bool vertical)
            where TSample : unmanaged
            where TFormat : struct, ISampleFormat<TSample>
        {
            int acrossEnd = vertical ? plane.Width : plane.Height;
            int alongEnd = vertical ? plane.Height : plane.Width;
            for (int across = 8; across < acrossEnd; across += 8)
            {
                for (int along = 0; along < alongEnd; along += 4)
                {
                    int x = vertical ? across : along;
                    int y = vertical ? along : across;
                    if (blocks.Boundary(x, y, vertical))
                    {
                        IntraSliceHeader current = blocks.Partitions?.SliceAt(x, y) ?? slice;
                        int px = vertical ? x - 1 : x;
                        int py = vertical ? y : y - 1;
                        if (!current.DeblockingDisabled && (blocks.Partitions is null || blocks.Partitions.CanFilter(x, y, px, py)))
                        {
                            LumaEdge<TSample, TFormat>(plane, current, blocks, x, y, vertical);
                        }
                    }
                }
            }
        }

        private static void LumaEdge<TSample, TFormat>(SamplePlaneView<TSample> plane, IntraSliceHeader slice, CodingBlockMap blocks, int x, int y, bool vertical)
            where TSample : unmanaged
            where TFormat : struct, ISampleFormat<TSample>
        {
            int px = vertical ? x - 1 : x;
            int py = vertical ? y : y - 1;
            int qp = (blocks.Qp(x, y) + blocks.Qp(px, py) + 1) >> 1;
            int depth = slice.Picture.Sequence.BitDepth;
            int beta = DeblockingThresholds.Beta(qp + slice.BetaOffset, depth);
            int tc = DeblockingThresholds.Tc(qp + 2 + slice.TcOffset, depth);
            if ((beta == 0) || (tc == 0))
            {
                return;
            }

            Span<int> samples = stackalloc int[32];
            ReadLumaEdge<TSample, TFormat>(plane, samples, x, y, vertical);
            DeblockingKernel.Luma(samples, beta, tc, depth, !blocks.Bypass(px, py), !blocks.Bypass(x, y));
            WriteLumaEdge<TSample, TFormat>(plane, samples, x, y, vertical);
        }

        private static void ReadLumaEdge<TSample, TFormat>(SamplePlaneView<TSample> plane, Span<int> samples, int x, int y, bool vertical)
            where TSample : unmanaged
            where TFormat : struct, ISampleFormat<TSample>
        {
            if (vertical)
            {
                for (int k = 0; k < 4; k++)
                {
                    ReadOnlySpan<TSample> row = plane.Row(y + k).Slice(x - 4, 8);
                    for (int i = 0; i < 8; i++)
                    {
                        samples[(k * 8) + i] = default(TFormat).Read(row[i]);
                    }
                }
            }
            else
            {
                for (int i = 0; i < 8; i++)
                {
                    ReadOnlySpan<TSample> row = plane.Row((y + i) - 4).Slice(x, 4);
                    for (int k = 0; k < 4; k++)
                    {
                        samples[(k * 8) + i] = default(TFormat).Read(row[k]);
                    }
                }
            }
        }

        private static void WriteLumaEdge<TSample, TFormat>(SamplePlaneView<TSample> plane, ReadOnlySpan<int> samples, int x, int y, bool vertical)
            where TSample : unmanaged
            where TFormat : struct, ISampleFormat<TSample>
        {
            if (vertical)
            {
                for (int k = 0; k < 4; k++)
                {
                    Span<TSample> row = plane.Row(y + k).Slice(x - 3, 6);
                    for (int i = 0; i < 6; i++)
                    {
                        row[i] = default(TFormat).Write(samples[(k * 8) + i + 1]);
                    }
                }
            }
            else
            {
                for (int i = 1; i <= 6; i++)
                {
                    Span<TSample> row = plane.Row((y + i) - 4).Slice(x, 4);
                    for (int k = 0; k < 4; k++)
                    {
                        row[k] = default(TFormat).Write(samples[(k * 8) + i]);
                    }
                }
            }
        }

        private static void ChromaEdges<TSample, TFormat>(SamplePlaneView<TSample> plane, IntraSliceHeader slice, CodingBlockMap blocks, bool vertical, int qpOffset)
            where TSample : unmanaged
            where TFormat : struct, ISampleFormat<TSample>
        {
            int acrossEnd = vertical ? plane.Width : plane.Height;
            int alongEnd = vertical ? plane.Height : plane.Width;
            int scaleX = slice.Picture.Sequence.SubWidthC;
            int scaleY = slice.Picture.Sequence.SubHeightC;
            Span<int> samples = stackalloc int[4];
            for (int across = 8; across < acrossEnd; across += 8)
            {
                for (int along = 0; along < alongEnd; along += 4)
                {
                    int x = vertical ? across : along;
                    int y = vertical ? along : across;
                    if (!blocks.Boundary(x * scaleX, y * scaleY, vertical))
                    {
                        continue;
                    }
                    ChromaEdge<TSample, TFormat>(plane, slice, blocks, vertical, qpOffset, x, y, samples);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int ChromaThreshold(IntraSliceHeader slice, CodingBlockMap blocks, int x, int y, int px, int py, int qpOffset)
        {
            IntraSliceHeader current = blocks.Partitions?.SliceAt(x, y) ?? slice;
            if (current.DeblockingDisabled || (blocks.Partitions is not null && !blocks.Partitions.CanFilter(x, y, px, py)))
            {
                return 0;
            }
            int qp = ((blocks.Qp(x, y) + blocks.Qp(px, py) + 1) >> 1) + qpOffset;
            int depth = slice.Picture.Sequence.BitDepth;
            return DeblockingThresholds.Tc(QuantizationMath.Chroma(qp, current.Picture.Sequence.ChromaFormat) + 2 + current.TcOffset, depth);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ChromaEdge<TSample, TFormat>(SamplePlaneView<TSample> plane, IntraSliceHeader slice,
            CodingBlockMap blocks, bool vertical, int qpOffset, int x, int y, Span<int> samples)
            where TSample : unmanaged
            where TFormat : struct, ISampleFormat<TSample>
        {
            int depth = slice.Picture.Sequence.BitDepth;
            int scaleX = slice.Picture.Sequence.SubWidthC;
            int scaleY = slice.Picture.Sequence.SubHeightC;
            int px = vertical ? (x * scaleX) - 1 : x * scaleX;
            int py = vertical ? y * scaleY : (y * scaleY) - 1;
            int tc = ChromaThreshold(slice, blocks, x * scaleX, y * scaleY, px, py, qpOffset);
            if (tc == 0)
            {
                return;
            }
            int alongX = vertical ? 0 : 1;
            int alongY = vertical ? 1 : 0;
            int acrossX = 1 - alongX;
            int acrossY = 1 - alongY;
            for (int k = 0; k < 4; k++)
            {
                for (int i = 0; i < 4; i++)
                {
                    samples[i] = default(TFormat).Read(plane.Row(y + (k * alongY) + ((i - 2) * acrossY))[x + (k * alongX) + ((i - 2) * acrossX)]);
                }

                bool writeP = !blocks.Bypass(px + (k * alongX * scaleX), py + (k * alongY * scaleY));
                bool writeQ = !blocks.Bypass((x + (k * alongX)) * scaleX, (y + (k * alongY)) * scaleY);
                DeblockingKernel.Chroma(samples, tc, depth, writeP, writeQ);
                for (int i = 1; i <= 2; i++)
                {
                    plane.Row(y + (k * alongY) + ((i - 2) * acrossY))[x + (k * alongX) + ((i - 2) * acrossX)] = default(TFormat).Write(samples[i]);
                }
            }
        }
    }
}
