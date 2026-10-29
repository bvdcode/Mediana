// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace Mediana.Images.Heif.Hevc
{
    /// <summary>
    /// Reads CTB offsets and applies them after deblocking using original neighbouring rows.
    /// </summary>
    /// <remarks>
    /// CTB parameters belong to the picture and are published with completed CTUs. Row storage is rented
    /// only during filtering and returned even when filtering fails. In-place writes never
    /// become neighbour inputs. CTB coordinates are in luma units; component coordinates
    /// follow the coded horizontal and vertical subsampling. Picture edges have no edge-offset neighbours.
    /// </remarks>
    internal class SaoFilter
    {
        private readonly IntraSliceHeader slice;
        private readonly SaoParameters[] parameters;
        private readonly int columns;
        private readonly int components;
        private readonly bool enabled;
        private readonly bool lumaEnabled;
        private readonly bool chromaEnabled;
        private readonly PicturePartitions? partitions;

        public SaoFilter(IntraSliceHeader slice, PicturePartitions? partitions = null)
        {
            this.slice = slice;
            this.partitions = partitions;
            int ctb = 1 << slice.Picture.Sequence.Log2Ctb;
            columns = ((slice.Picture.Sequence.Width + ctb) - 1) / ctb;
            int rows = ((slice.Picture.Sequence.Height + ctb) - 1) / ctb;
            components = slice.Picture.Sequence.ChromaFormat == 0 ? 1 : 3;
            lumaEnabled = slice.SaoLuma;
            chromaEnabled = slice.SaoChroma;
            if (partitions is not null)
            {
                foreach (IntraSliceHeader segment in partitions.Slices)
                {
                    lumaEnabled |= segment.SaoLuma;
                    chromaEnabled |= segment.SaoChroma;
                }
            }
            enabled = lumaEnabled || chromaEnabled;
            parameters = enabled ? new SaoParameters[checked(columns * rows * components)] : Array.Empty<SaoParameters>();
        }

        public void ReadCtb(int x, int y, IntraCabacReader reader)
        {
            if (!enabled)
            {
                return;
            }
            int ctb = 1 << slice.Picture.Sequence.Log2Ctb;
            IntraSliceHeader currentSlice = partitions?.SliceAt(x * ctb, y * ctb) ?? slice;
            if (!currentSlice.SaoLuma && !currentSlice.SaoChroma)
            {
                return;
            }
            int current = ((y * columns) + x) * components;
            int previous = ReadMerge(x, y, current, ctb, reader);
            if (previous >= 0)
            {
                Array.Copy(parameters, previous, parameters, current, components);
                return;
            }

            for (int component = 0; component < components; component++)
            {
                if (component == 0 ? currentSlice.SaoLuma : currentSlice.SaoChroma)
                {
                    parameters[current + component] = SaoParameters.Read(reader, component, slice.Picture.Sequence.BitDepth,
                        component == 2 ? parameters[current + 1] : default);
                }
            }
        }

        public void Apply(DecodedPicture picture, CodingBlockMap blocks)
        {
            if (!enabled)
            {
                return;
            }
            if (lumaEnabled)
            {
                ApplyPlane(picture.Luma, blocks, 0);
            }
            if (chromaEnabled && picture.Cb is not null)
            {
                ApplyPlane(picture.Cb, blocks, 1);
            }
            if (chromaEnabled && picture.Cr is not null)
            {
                ApplyPlane(picture.Cr, blocks, 2);
            }
        }

        private void ApplyPlane(SamplePlane plane, CodingBlockMap blocks, int component)
        {
            switch (plane.BitDepth)
            {
                case 8:
                    ApplyPlane<byte, ByteSampleFormat>(plane.ByteView, blocks, component);
                    break;
                case 10:
                    ApplyPlane<ushort, WordSampleFormat>(plane.WordView, blocks, component);
                    break;
                default:
                    throw new InvalidOperationException("Unsupported component sample depth.");
            }
        }

        private void ApplyPlane<TSample, TFormat>(SamplePlaneView<TSample> plane, CodingBlockMap blocks, int component)
            where TSample : unmanaged
            where TFormat : struct, ISampleFormat<TSample>
        {
            int depth = slice.Picture.Sequence.BitDepth;
            int scaleX = component == 0 ? 1 : slice.Picture.Sequence.SubWidthC;
            int scaleY = component == 0 ? 1 : slice.Picture.Sequence.SubHeightC;
            int ctbWidth = (1 << slice.Picture.Sequence.Log2Ctb) / scaleX;
            int ctbHeight = (1 << slice.Picture.Sequence.Log2Ctb) / scaleY;
            TSample[] buffer = ArrayPool<TSample>.Shared.Rent(checked(plane.Width * 2));
            try
            {
                for (int y = 0; y < plane.Height; y++)
                {
                    Span<TSample> current = buffer.AsSpan((y & 1) * plane.Width, plane.Width);
                    plane.Row(y).CopyTo(current);
                    ReadOnlySpan<TSample> above = y == 0 ? ReadOnlySpan<TSample>.Empty : buffer.AsSpan(((y - 1) & 1) * plane.Width, plane.Width);
                    ReadOnlySpan<TSample> below = (y + 1) == plane.Height ? ReadOnlySpan<TSample>.Empty : plane.Row(y + 1);
                    Span<TSample> output = plane.Row(y);
                    int parameterRow = ((y / ctbHeight) * columns * components) + component;
                    for (int start = 0, index = parameterRow; start < plane.Width; start += ctbWidth, index += components)
                    {
                        SaoParameters parameter = parameters[index];
                        if (parameter.Type == 0)
                        {
                            continue;
                        }

                        int end = Math.Min(start + ctbWidth, plane.Width);
                        if (parameter.Type == 1)
                        {
                            for (int x = start; x < end; x++)
                            {
                                if (!blocks.Bypass(x * scaleX, y * scaleY))
                                {
                                    output[x] = default(TFormat).Write(SaoKernel.Band(default(TFormat).Read(current[x]), depth, parameter));
                                }
                            }
                        }
                        else
                        {
                            ApplyEdge<TSample, TFormat>(plane, blocks, parameter, output, above, current, below,
                                y, start, end, scaleX, scaleY, depth);
                        }
                    }
                }
            }
            finally
            {
                ArrayPool<TSample>.Shared.Return(buffer);
            }
        }

        private static ReadOnlySpan<TSample> NeighbourRow<TSample>(ReadOnlySpan<TSample> above, ReadOnlySpan<TSample> current, ReadOnlySpan<TSample> below, int dy)
            where TSample : unmanaged
        {
            switch (dy)
            {
                case -1:
                    return above;
                case 0:
                    return current;
                case 1:
                    return below;
                default:
                    throw new ArgumentOutOfRangeException(nameof(dy));
            }
        }

        private static void Direction(int edge, out int dx, out int dy)
        {
            switch (edge)
            {
                case 0:
                    dx = -1;
                    dy = 0;
                    break;
                case 1:
                    dx = 0;
                    dy = -1;
                    break;
                case 2:
                    dx = -1;
                    dy = -1;
                    break;
                case 3:
                    dx = 1;
                    dy = -1;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(edge));
            }
        }

        private int ReadMerge(int x, int y, int current, int ctb, IntraCabacReader reader)
        {
            int previous = -1;
            if ((x > 0) && (partitions is null || partitions.SameSlice((x - 1) * ctb, y * ctb, x * ctb, y * ctb)) && reader.Read(CabacElement.SaoMerge))
            {
                previous = current - components;
            }
            else if ((y > 0) && (partitions is null || partitions.SameSlice(x * ctb, (y - 1) * ctb, x * ctb, y * ctb)) && reader.Read(CabacElement.SaoMerge))
            {
                previous = current - (columns * components);
            }
            return previous;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ApplyEdge<TSample, TFormat>(SamplePlaneView<TSample> plane, CodingBlockMap blocks, SaoParameters parameter,
            Span<TSample> output, ReadOnlySpan<TSample> above, ReadOnlySpan<TSample> current, ReadOnlySpan<TSample> below,
            int y, int start, int end, int scaleX, int scaleY, int depth)
            where TSample : unmanaged
            where TFormat : struct, ISampleFormat<TSample>
        {
            Direction(parameter.Edge, out int dx, out int dy);
            if ((y < Math.Abs(dy)) || (y >= (plane.Height - Math.Abs(dy))))
            {
                return;
            }

            ReadOnlySpan<TSample> first = NeighbourRow<TSample>(above, current, below, dy);
            ReadOnlySpan<TSample> second = NeighbourRow<TSample>(above, current, below, -dy);
            int firstX = Math.Max(start, Math.Abs(dx));
            int lastX = Math.Min(end, plane.Width - Math.Abs(dx));
            for (int x = firstX; x < lastX; x++)
            {
                if (!blocks.Bypass(x * scaleX, y * scaleY) &&
                    (partitions is null ||
                        (partitions.CanFilter(x * scaleX, y * scaleY, (x + dx) * scaleX, (y + dy) * scaleY) &&
                         partitions.CanFilter(x * scaleX, y * scaleY, (x - dx) * scaleX, (y - dy) * scaleY))))
                {
                    output[x] = default(TFormat).Write(SaoKernel.Edge(default(TFormat).Read(current[x]),
                        default(TFormat).Read(first[x + dx]), default(TFormat).Read(second[x - dx]), depth, parameter));
                }
            }
        }
    }
}
