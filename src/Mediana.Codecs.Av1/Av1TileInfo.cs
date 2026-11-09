// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal readonly record struct Av1TileInfo(int ColumnLog2, int RowLog2, int ContextUpdateTile, int SizeBytes, Av1TileGrid? Grid)
    {
        private const int MaximumWidth = 4096;
        private const int MaximumArea = 4096 * 2304;
        private const int MaximumColumns = 64;
        private const int MaximumRows = 64;

        internal int Columns => Grid?.Columns ?? 1;
        internal int Rows => Grid?.Rows ?? 1;
        internal int Count => Columns * Rows;

        internal Av1TileBounds Bounds(int index, Av1SequenceHeader sequence)
        {
            if (Grid is not null) { return Grid.Bounds(index); }
            if (index != 0) { throw new ArgumentOutOfRangeException(nameof(index)); }
            return new(0, sequence.MiRows, 0, sequence.MiColumns);
        }

        internal static Av1TileInfo Read(ref Av1BitReader bits, Av1SequenceHeader sequence)
        {
            int shift = sequence.Superblock128 ? 5 : 4;
            int sbColumns = (sequence.MiColumns + (1 << shift) - 1) >> shift;
            int sbRows = (sequence.MiRows + (1 << shift) - 1) >> shift;
            int maxWidth = MaximumWidth >> (shift + 2);
            int maxArea = MaximumArea >> (2 * (shift + 2));
            int minimumColumns = TileLog2(maxWidth, sbColumns);
            int maximumColumns = TileLog2(1, Math.Min(sbColumns, MaximumColumns));
            int maximumRows = TileLog2(1, Math.Min(sbRows, MaximumRows));
            int minimumTiles = Math.Max(minimumColumns, TileLog2(maxArea, checked(sbColumns * sbRows)));
            Span<int> columns = stackalloc int[MaximumColumns + 1];
            Span<int> rows = stackalloc int[MaximumRows + 1];
            int columnLog2;
            int rowLog2;
            int columnCount;
            int rowCount;
            if (bits.Flag())
            {
                columnLog2 = minimumColumns;
                while (columnLog2 < maximumColumns && bits.Flag()) { columnLog2++; }
                int width = (sbColumns + (1 << columnLog2) - 1) >> columnLog2;
                columnCount = UniformStarts(columns, sbColumns, width, shift, sequence.MiColumns);
                rowLog2 = Math.Max(minimumTiles - columnLog2, 0);
                while (rowLog2 < maximumRows && bits.Flag()) { rowLog2++; }
                int height = (sbRows + (1 << rowLog2) - 1) >> rowLog2;
                rowCount = UniformStarts(rows, sbRows, height, shift, sequence.MiRows);
            }
            else
            {
                columnCount = NonuniformStarts(ref bits, columns, sbColumns, maxWidth, shift, sequence.MiColumns, out int widest);
                columnLog2 = TileLog2(1, columnCount);
                maxArea = minimumTiles > 0 ? (sbColumns * sbRows) >> (minimumTiles + 1) : sbColumns * sbRows;
                int maxHeight = Math.Max(maxArea / widest, 1);
                rowCount = NonuniformStarts(ref bits, rows, sbRows, maxHeight, shift, sequence.MiRows, out _);
                rowLog2 = TileLog2(1, rowCount);
            }
            int contextTile = 0;
            int sizeBytes = 0;
            if (columnLog2 + rowLog2 > 0)
            {
                contextTile = bits.Read(columnLog2 + rowLog2);
                sizeBytes = bits.Read(2) + 1;
                if (contextTile >= columnCount * rowCount)
                {
                    throw new InvalidDataException("The AV1 context-update tile index is outside the tile grid.");
                }
            }
            Av1TileGrid? grid = columnCount * rowCount == 1 ? null : new(columns[..(columnCount + 1)], rows[..(rowCount + 1)]);
            return new(columnLog2, rowLog2, contextTile, sizeBytes, grid);
        }

        private static int UniformStarts(Span<int> starts, int length, int size, int shift, int end)
        {
            int count = 0;
            for (int start = 0; start < length; start += size)
            {
                if (count == starts.Length - 1) { throw new InvalidDataException("The AV1 tile grid exceeds its maximum extent."); }
                starts[count++] = start << shift;
            }
            starts[count] = end;
            return count;
        }

        private static int NonuniformStarts(ref Av1BitReader bits, scoped Span<int> starts, int length, int maximum, int shift, int end, out int widest)
        {
            int count = 0;
            int start = 0;
            widest = 0;
            while (start < length)
            {
                if (count == starts.Length - 1) { throw new InvalidDataException("The AV1 tile grid exceeds its maximum extent."); }
                starts[count++] = start << shift;
                int size = bits.ReadNs(Math.Min(length - start, maximum)) + 1;
                start += size;
                widest = Math.Max(widest, size);
            }
            starts[count] = end;
            return count;
        }

        private static int TileLog2(int size, int target)
        {
            int result = 0;
            while (((long)size << result) < target) { result++; }
            return result;
        }
    }
}
