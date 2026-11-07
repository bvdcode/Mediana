// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core.Memory;
using Mediana.Formats.Video;

namespace Mediana.Codecs.Theora
{
    internal class TheoraLayout : IDisposable
    {
        private readonly WritableMediaBuffer storage;
        internal TheoraIdentification Info { get; }
        internal int LumaBlocks { get; }
        internal int ChromaBlocks { get; }
        internal int BlockCount { get; }
        internal int MacroCount { get; }
        internal int SuperCount { get; }
        internal int SampleCount => checked(BlockCount * 64);
        private Span<int> Data => MemoryMarshal.Cast<byte, int>(storage.Span);
        internal ReadOnlySpan<int> CodedToRaster => Data[..BlockCount];
        internal ReadOnlySpan<int> RasterToCoded => Data.Slice(BlockCount, BlockCount);
        internal ReadOnlySpan<int> MacroOrder => Data.Slice(BlockCount * 2, MacroCount);
        internal ReadOnlySpan<int> SuperStarts => Data.Slice(BlockCount * 2 + MacroCount, SuperCount + 1);

        internal TheoraLayout(TheoraIdentification info, MediaBufferAllocator allocator)
        {
            Info = info;
            LumaBlocks = checked(info.CodedWidth / 8 * (info.CodedHeight / 8));
            ChromaBlocks = LumaBlocks / (info.HorizontalSubsampling * info.VerticalSubsampling);
            BlockCount = checked(LumaBlocks + ChromaBlocks * 2);
            MacroCount = LumaBlocks / 4;
            SuperCount = checked(((info.CodedWidth + 31) / 32) * ((info.CodedHeight + 31) / 32) +
                2 * ((PlaneWidth(1) + 31) / 32) * ((PlaneHeight(1) + 31) / 32));
            storage = allocator.Allocate(checked((BlockCount * 2 + MacroCount + SuperCount + 1) * 4));
            bool success = false;
            try { Initialize(); success = true; }
            finally { if (!success) { storage.Dispose(); } }
        }

        private void Initialize()
        {
            ReadOnlySpan<byte> hx = [0, 1, 1, 0, 0, 0, 1, 1, 2, 2, 3, 3, 3, 2, 2, 3];
            ReadOnlySpan<byte> hy = [0, 0, 1, 1, 2, 3, 3, 2, 2, 3, 3, 2, 1, 1, 0, 0];
            Span<int> code = Data[..BlockCount], raster = Data.Slice(BlockCount, BlockCount);
            Span<int> supers = Data.Slice(BlockCount * 2 + MacroCount, SuperCount + 1);
            int block = 0, super = 0;
            for (int plane = 0; plane < 3; plane++)
            {
                int width = PlaneWidth(plane) / 8, height = PlaneHeight(plane) / 8, offset = PlaneBlockOffset(plane);
                for (int y = 0; y < height; y += 4)
                {
                    for (int x = 0; x < width; x += 4)
                    {
                        supers[super++] = block;
                        for (int h = 0; h < 16; h++)
                        {
                            int bx = x + hx[h], by = y + hy[h];
                            if (bx >= width || by >= height) { continue; }
                            int index = offset + by * width + bx; code[block] = index; raster[index] = block++;
                        }
                    }
                }
            }
            supers[super] = block;
            Span<int> macros = Data.Slice(BlockCount * 2, MacroCount);
            int columns = Info.CodedWidth / 16, rows = Info.CodedHeight / 16, macro = 0;
            ReadOnlySpan<byte> mx = [0, 0, 1, 1], my = [0, 1, 1, 0];
            for (int y = 0; y < rows; y += 2)
            {
                for (int x = 0; x < columns; x += 2)
                {
                    for (int h = 0; h < 4; h++)
                    {
                        int bx = x + mx[h], by = y + my[h];
                        if (bx < columns && by < rows) { macros[macro++] = by * columns + bx; }
                    }
                }
            }
        }

        internal int Plane(int coded) => coded < LumaBlocks ? 0 : coded < LumaBlocks + ChromaBlocks ? 1 : 2;
        internal int PlaneWidth(int plane) => plane == 0 ? Info.CodedWidth : Info.CodedWidth / Info.HorizontalSubsampling;
        internal int PlaneHeight(int plane) => plane == 0 ? Info.CodedHeight : Info.CodedHeight / Info.VerticalSubsampling;
        internal int PlaneBlockOffset(int plane) => plane switch { 0 => 0, 1 => LumaBlocks, 2 => LumaBlocks + ChromaBlocks, _ => throw new ArgumentOutOfRangeException(nameof(plane)) };
        internal int PlaneOffset(int plane) => checked(PlaneBlockOffset(plane) * 64);
        internal (int X, int Y) Coordinates(int coded)
        {
            int plane = Plane(coded), raster = CodedToRaster[coded] - PlaneBlockOffset(plane), width = PlaneWidth(plane) / 8;
            return (raster % width, raster / width);
        }
        internal int Macro(int coded)
        {
            int plane = Plane(coded); (int x, int y) = Coordinates(coded);
            if (plane != 0) { x *= Info.HorizontalSubsampling; y *= Info.VerticalSubsampling; }
            return y / 2 * (Info.CodedWidth / 16) + x / 2;
        }
        internal int Block(int plane, int x, int y) => RasterToCoded[PlaneBlockOffset(plane) + y * (PlaneWidth(plane) / 8) + x];
        public void Dispose() => storage.Dispose();
    }
}
