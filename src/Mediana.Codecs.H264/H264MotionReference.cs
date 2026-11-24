// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using Mediana.Core.Memory;

namespace Mediana.Codecs.H264
{
    internal class H264MotionReference : IDisposable
    {
        internal const int LocalSearchRange = 16;
        private const int TileWidth = 64;
        private const int TileHeight = 32;
        private readonly int tileColumns;
        private readonly int tileCount;
        private readonly WritableMediaBuffer storage;
        private readonly int sampleBytes;
        private readonly int planeBytes;
        private readonly int rows;
        private readonly int pictureWidth;
        private readonly int pictureHeight;
        private readonly int quarterBytes;

        public H264MotionReference(H264Picture picture, int range, MediaBufferAllocator allocator)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(range);
            Range = range;
            pictureWidth = picture.Width;
            pictureHeight = picture.Height;
            HalfStride = checked(picture.Width + range * 2 + 1);
            rows = checked(picture.Height + range * 2 + 1);
            SampleStride = checked(HalfStride + 5);
            sampleBytes = checked(SampleStride * (rows + 5));
            planeBytes = checked(HalfStride * rows);
            tileColumns = (HalfStride + TileWidth - 1) / TileWidth;
            tileCount = checked(tileColumns * ((rows + TileHeight - 1) / TileHeight));
            quarterBytes = range > LocalSearchRange ? checked((pictureWidth / H264QuarterLuma.Scale) * (pictureHeight / H264QuarterLuma.Scale)) : 0;
            storage = allocator.Allocate(checked(sampleBytes + planeBytes * 3 + TileWidth * 12 * sizeof(short) + quarterBytes + tileCount));
        }

        private Span<byte> QuarterPlane => storage.Span[^(tileCount + quarterBytes)..^tileCount];
        public ReadOnlySpan<byte> QuarterLuma => QuarterPlane;
        public bool HasQuarterLuma => quarterBytes != 0;
        public int Range { get; }
        public int SampleStride { get; }
        public int HalfStride { get; }
        public ReadOnlySpan<byte> Samples => storage.Span[..sampleBytes];
        public ReadOnlySpan<byte> Horizontal => storage.Span.Slice(sampleBytes, planeBytes);
        public ReadOnlySpan<byte> Vertical => storage.Span.Slice(sampleBytes + planeBytes, planeBytes);
        public ReadOnlySpan<byte> Diagonal => storage.Span.Slice(sampleBytes + planeBytes * 2, planeBytes);

        public void Prepare(H264Picture picture, CancellationToken token)
        {
            if (picture.Width != pictureWidth || picture.Height != pictureHeight)
            {
                throw new ArgumentException("The motion reference dimensions changed.", nameof(picture));
            }
            token.ThrowIfCancellationRequested();
            if (HasQuarterLuma)
            {
                H264QuarterLuma.Reduce(picture.ReadPlane(0), pictureWidth, pictureHeight, QuarterPlane, token);
            }
            Span<byte> samples = storage.Span[..sampleBytes];
            H264BorderExtension.Copy(picture.ReadPlane(0), picture.Width, picture.Height,
                -Range - 2, -Range - 2, SampleStride, rows + 5, samples);
            storage.Span[^tileCount..].Clear();
        }

        public void PrepareNeighborhood(int x, int y)
        {
            int left = Math.Max(0, x + Range - 1) / TileWidth;
            int right = Math.Min(HalfStride - 1, x + Range + 16) / TileWidth;
            int top = Math.Max(0, y + Range - 1) / TileHeight;
            int bottom = Math.Min(rows - 1, y + Range + 16) / TileHeight;
            Span<byte> ready = storage.Span[^tileCount..];
            for (int row = top; row <= bottom; row++)
            {
                for (int column = left; column <= right; column++)
                {
                    int tile = row * tileColumns + column;
                    if (ready[tile] == 0)
                    {
                        FilterTile(column * TileWidth, row * TileHeight);
                        ready[tile] = 1;
                    }
                }
            }
        }

        private void FilterTile(int x, int y)
        {
            int width = Math.Min(TileWidth, HalfStride - x);
            int height = Math.Min(TileHeight, rows - y);
            int output = y * HalfStride + x;
            ReadOnlySpan<byte> samples = Samples[(y * SampleStride + x)..];
            Span<byte> horizontal = storage.Span.Slice(sampleBytes + output, planeBytes - output);
            Span<byte> vertical = storage.Span.Slice(sampleBytes + planeBytes + output, planeBytes - output);
            Span<byte> diagonal = storage.Span.Slice(sampleBytes + planeBytes * 2 + output, planeBytes - output);
            if (H264ConstantRegion.TryRead(samples, SampleStride, width + 5, height + 5, out byte value))
            {
                for (int row = 0; row < height; row++)
                {
                    horizontal.Slice(row * HalfStride, width).Fill(value);
                    vertical.Slice(row * HalfStride, width).Fill(value);
                    diagonal.Slice(row * HalfStride, width).Fill(value);
                }
                return;
            }
            Span<short> intermediate = MemoryMarshal.Cast<byte, short>(storage.Span.Slice(sampleBytes + planeBytes * 3, TileWidth * 12 * sizeof(short)));
            for (int row = 0; row < height + 5; row++)
            {
                int start = row % 6 * TileWidth;
                int column = 0;
                for (; column <= width - Vector128<short>.Count; column += Vector128<short>.Count)
                {
                    H264LumaFilter.Sum8(samples, row * SampleStride + column, 1)
                        .StoreUnsafe(ref MemoryMarshal.GetReference(intermediate), (nuint)(start + column));
                }
                for (; column < width; column++)
                {
                    intermediate[start + column] = (short)H264LumaFilter.Sum(samples, row * SampleStride + column, 1);
                }
                intermediate.Slice(start, width).CopyTo(intermediate[(start + 6 * TileWidth)..]);
                if (row >= 5)
                {
                    int filtered = row - 5;
                    FilterRow(samples[(filtered * SampleStride)..], intermediate[(filtered % 6 * TileWidth)..],
                        horizontal[(filtered * HalfStride)..], vertical[(filtered * HalfStride)..], diagonal[(filtered * HalfStride)..], width);
                }
            }
        }

        private void FilterRow(ReadOnlySpan<byte> samples, ReadOnlySpan<short> intermediate,
            Span<byte> horizontal, Span<byte> vertical, Span<byte> diagonal, int width)
        {
            int column = 0;
            for (; column <= width - Vector128<short>.Count; column += Vector128<short>.Count)
            {
                Vector128<short> sum = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(intermediate), (nuint)(2 * TileWidth + column));
                H264VectorSamples.Write8(horizontal, column, H264LumaFilter.Half8(sum));
                H264VectorSamples.Write8(vertical, column,
                    H264LumaFilter.Half8(H264LumaFilter.Sum8(samples, column + 2, SampleStride)));
                H264VectorSamples.Write8(diagonal, column, H264LumaFilter.Diagonal8(intermediate, TileWidth, column));
            }
            for (; column < width; column++)
            {
                horizontal[column] = (byte)H264LumaInterpolation.Half(intermediate[2 * TileWidth + column]);
                vertical[column] = (byte)H264LumaInterpolation.Half(H264LumaFilter.Sum(samples, column + 2, SampleStride));
                diagonal[column] = (byte)H264LumaFilter.Diagonal(intermediate, TileWidth, column);
            }
        }

        public void Dispose() => storage.Dispose();
    }
}
