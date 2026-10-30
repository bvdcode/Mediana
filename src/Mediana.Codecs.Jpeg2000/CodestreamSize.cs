// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Jpeg2000
{
    internal record CodestreamSize(int X0, int Y0, int X1, int Y1, int TileX, int TileY, int TileWidth,
        int TileHeight, int Across, int TileCount, ComponentSize[] Components)
    {
        internal static CodestreamSize Read(ByteReader input, ContainerInfo container, DecodeBudget budget)
        {
            budget.Cancellation.ThrowIfCancellationRequested();
            if (input.Marker() != 0xff4f || input.Marker() != 0xff51)
            {
                throw new InvalidDataException("JPEG 2000 must start with SOC and SIZ.");
            }
            ByteReader data = input.Segment();
            if (data.U16() > 2)
            {
                throw new NotSupportedException("JPEG 2000 extended profile.");
            }
            int x1 = Dimension(data), y1 = Dimension(data), x0 = Dimension(data), y0 = Dimension(data);
            int tileWidth = Dimension(data), tileHeight = Dimension(data), tileX = Dimension(data), tileY = Dimension(data);
            budget.Pixels((long)x1 - x0, (long)y1 - y0, true);
            if (tileWidth == 0 || tileHeight == 0 || tileX > x0 || tileY > y0 || (long)tileX + tileWidth <= x0 || (long)tileY + tileHeight <= y0)
            {
                throw new InvalidDataException("Invalid JPEG 2000 tile grid.");
            }
            int across = ByteReader.Ceil(x1 - tileX, tileWidth);
            long tiles = (long)across * ByteReader.Ceil(y1 - tileY, tileHeight);
            if (tiles > 65535)
            {
                throw new NotSupportedException("JPEG 2000 tile count exceeds 65535.");
            }
            int count = data.U16();
            if (count < 1 || count > 16)
            {
                throw new NotSupportedException("JPEG 2000 supports 1 to 16 components.");
            }
            budget.Reserve(count * 64L);
            ComponentSize[] components = new ComponentSize[count];
            for (int c = 0; c < count; c++)
            {
                components[c] = ReadComponent(data, budget, x0, y0, x1, y1);
            }
            data.End();
            if (container.Components != 0 && (container.Components != count || container.Width != x1 - x0 || container.Height != y1 - y0))
            {
                throw new InvalidDataException("JP2 header dimensions disagree with the codestream.");
            }
            if (container.Alpha >= count)
            {
                throw new InvalidDataException("Invalid JP2 alpha component.");
            }
            return new CodestreamSize(x0, y0, x1, y1, tileX, tileY, tileWidth, tileHeight, across, (int)tiles, components);
        }

        private static ComponentSize ReadComponent(ByteReader data, DecodeBudget budget, int x0, int y0, int x1, int y1)
        {
            int precision = data.U8(), dx = data.U8(), dy = data.U8();
            int bits = (precision & 127) + 1;
            if (bits > 16)
            {
                throw new NotSupportedException("JPEG 2000 sample precision above 16 bits.");
            }
            if (dx == 0 || dy == 0)
            {
                throw new InvalidDataException("Invalid JPEG 2000 component sampling.");
            }
            int left = ByteReader.Ceil(x0, dx), top = ByteReader.Ceil(y0, dy);
            int width = ByteReader.Ceil(x1, dx) - left, height = ByteReader.Ceil(y1, dy) - top;
            budget.Pixels(width, height);
            return new ComponentSize(left, top, width, height, bits, (precision & 128) != 0, dx, dy);
        }

        private static int Dimension(ByteReader input)
        {
            uint value = input.U32();
            if (value > int.MaxValue)
            {
                throw new InvalidDataException("JPEG 2000 coordinate exceeds supported range.");
            }
            return (int)value;
        }
    }
}
