// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using Mediana.Core.Video;

namespace Mediana.Codecs.H264
{
    internal readonly ref struct H264SourcePicture(VideoFrame frame)
    {
        private readonly ReadOnlySpan<byte> pixels = frame.Data.Span;
        private readonly PlaneLayout luma = frame.Planes[0];
        private readonly PlaneLayout blue = frame.Planes[1];
        private readonly PlaneLayout red = frame.Planes[2];

        public void Read(int x, int y, Span<byte> destination)
        {
            Copy<Vector128<byte>>(luma, x, y, destination[..256]);
            Copy<ulong>(blue, x / 2, y / 2, destination.Slice(256, 64));
            Copy<ulong>(red, x / 2, y / 2, destination.Slice(320, 64));
        }

        public int MatchingBlocks(int plane, int x, int y, int size, int blocks, ReadOnlySpan<byte> prior, int priorStride)
        {
            PlaneLayout layout = plane switch { 0 => luma, 1 => blue, 2 => red, _ => throw new ArgumentOutOfRangeException(nameof(plane)) };
            int rows = Math.Min(size, layout.Height - y);
            int visible = layout.RowBytes - x;
            for (int row = 0; row < rows && blocks > 0; row++)
            {
                int length = Math.Min(visible, blocks * size);
                ReadOnlySpan<byte> current = pixels.Slice(layout.Offset + (y + row) * layout.Stride + x, length);
                int prefix = current.CommonPrefixLength(prior.Slice(row * priorStride, length));
                if (prefix < length) { blocks = prefix / size; }
            }
            return blocks;
        }

        private void Copy<TRow>(PlaneLayout plane, int x, int y, Span<byte> destination) where TRow : unmanaged
        {
            int size = Unsafe.SizeOf<TRow>();
            if (x >= 0 && x <= plane.RowBytes - size && y >= 0 && y <= plane.Height - size)
            {
                int start = plane.Offset + y * plane.Stride + x;
                ReadOnlySpan<byte> source = pixels.Slice(start, (size - 1) * plane.Stride + size);
                Span<TRow> rows = MemoryMarshal.Cast<byte, TRow>(destination);
                for (int row = 0; row < size; row++)
                {
                    rows[row] = MemoryMarshal.Read<TRow>(source[(row * plane.Stride)..]);
                }
                return;
            }
            H264BorderExtension.Copy(pixels[plane.Offset..], plane.Stride, plane.RowBytes, plane.Height, x, y, size, size, destination);
        }
    }
}
