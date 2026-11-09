// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Hevc
{
    internal class HevcTileLayout
    {
        private readonly int[] rasterToTileScan;
        private readonly int[] tileScanToRaster;
        private readonly int[] tileIds;
        private readonly HevcTile[] tiles;

        internal HevcTileLayout(int columns, int rows, int[] columnWidths, int[] rowHeights, bool enabled)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(columns);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rows);
            ValidateSizes(columnWidths, columns);
            ValidateSizes(rowHeights, rows);
            Columns = columns;
            Rows = rows;
            Count = checked(columns * rows);
            Enabled = enabled;
            ColumnCount = columnWidths.Length;
            RowCount = rowHeights.Length;
            if (!enabled && (ColumnCount != 1 || RowCount != 1))
            {
                throw new InvalidDataException("Disabled HEVC tiles require one picture-wide tile.");
            }
            tiles = new HevcTile[checked(ColumnCount * RowCount)];
            rasterToTileScan = enabled ? new int[Count] : [];
            tileScanToRaster = enabled ? new int[Count] : [];
            tileIds = enabled ? new int[Count] : [];
            int tileId = 0, tileAddress = 0, top = 0;
            foreach (int height in rowHeights)
            {
                int left = 0;
                foreach (int width in columnWidths)
                {
                    HevcTile tile = new(left, top, width, height);
                    tiles[tileId] = tile;
                    if (enabled)
                    {
                        for (int y = top; y < tile.Bottom; y++)
                        {
                            for (int x = left; x < tile.Right; x++)
                            {
                                int rasterAddress = y * columns + x;
                                rasterToTileScan[rasterAddress] = tileAddress;
                                tileScanToRaster[tileAddress++] = rasterAddress;
                                tileIds[rasterAddress] = tileId;
                            }
                        }
                    }
                    tileId++;
                    left += width;
                }
                top += height;
            }
        }

        public bool Enabled { get; }
        public int Columns { get; }
        public int Rows { get; }
        public int Count { get; }
        public int ColumnCount { get; }
        public int RowCount { get; }
        public int MaximumSubstreams(bool wavefront) => wavefront ? checked(ColumnCount * Rows) : tiles.Length;
        public int ToRaster(int tileAddress) => Enabled ? tileScanToRaster[tileAddress] : tileAddress;
        public int ToTileScan(int rasterAddress) => Enabled ? rasterToTileScan[rasterAddress] : rasterAddress;
        public int TileId(int rasterAddress) => Enabled ? tileIds[rasterAddress] : 0;
        public HevcTile TileAt(int rasterAddress) => tiles[TileId(rasterAddress)];

        public bool StartsTile(int rasterAddress)
        {
            HevcTile tile = TileAt(rasterAddress);
            return rasterAddress == tile.Top * Columns + tile.Left;
        }

        public bool StartsSubstream(int rasterAddress, bool wavefront) =>
            wavefront ? rasterAddress % Columns == TileAt(rasterAddress).Left : StartsTile(rasterAddress);

        public static HevcTileLayout Read(BitReader reader, SequenceParameters sequence, bool enabled)
        {
            int ctbSize = 1 << sequence.Log2Ctb;
            int columns = (sequence.Width - 1) / ctbSize + 1;
            int rows = (sequence.Height - 1) / ctbSize + 1;
            if (!enabled) { return new(columns, rows, [columns], [rows], false); }
            int columnCount = HevcSyntax.Ue(reader, columns - 1, "num_tile_columns_minus1") + 1;
            int rowCount = HevcSyntax.Ue(reader, rows - 1, "num_tile_rows_minus1") + 1;
            bool uniform = reader.ReadFlag();
            int[] widths = ReadSizes(reader, columns, columnCount, uniform, "column_width_minus1");
            int[] heights = ReadSizes(reader, rows, rowCount, uniform, "row_height_minus1");
            return new(columns, rows, widths, heights, true);
        }

        private static int[] ReadSizes(BitReader reader, int extent, int count, bool uniform, string field)
        {
            int[] sizes = new int[count];
            int remaining = extent;
            for (int index = 0; index < count; index++)
            {
                if (uniform)
                {
                    sizes[index] = (int)((long)(index + 1) * extent / count - (long)index * extent / count);
                }
                else if (index + 1 == count)
                {
                    sizes[index] = remaining;
                }
                else
                {
                    int maximum = remaining - (count - index);
                    sizes[index] = HevcSyntax.Ue(reader, maximum, field) + 1;
                }
                remaining -= sizes[index];
            }
            return sizes;
        }

        private static void ValidateSizes(int[] sizes, int extent)
        {
            if (sizes.Length == 0 || sizes.Any(size => size <= 0) || sizes.Sum(size => (long)size) != extent)
            {
                throw new InvalidDataException("HEVC tile dimensions must cover the picture with positive widths and heights.");
            }
        }
    }
}
