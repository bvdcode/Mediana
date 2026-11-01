// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.IO;
using System.Threading;

namespace Mediana.Codecs.Jpeg2000
{
    internal class CodestreamDecoder
    {
        private readonly ByteReader _input;
        private readonly ContainerInfo _info;
        private readonly DecodeBudget _budget;
        private readonly HeaderSettings _main = new();
        private Jpeg2000Component[] _components = Array.Empty<Jpeg2000Component>();
        private Tile?[] _tiles = Array.Empty<Tile?>();
        private int _x0, _y0, _x1, _y1, _tileX, _tileY, _tileWidth, _tileHeight, _across;
        internal CodestreamDecoder(ReadOnlyMemory<byte> input, ContainerInfo info, DecodeBudget budget)
        {
            _input = new ByteReader(input);
            _info = info;
            _budget = budget;
        }

        internal Jpeg2000Image Decode()
        {
            Initialize(CodestreamSize.Read(_input, _info, _budget));
            bool end = false;
            while (_input.Remaining > 0)
            {
                _budget.Cancellation.ThrowIfCancellationRequested();
                int start = _input.Position;
                int marker = _input.Marker();
                if (marker == 0xffd9)
                {
                    end = true;
                    break;
                }

                if (marker == 0xff90)
                {
                    ReadTile(start);
                }
                else
                {
                    if (Array.Exists(_tiles, t => t is not null))
                    {
                        throw new InvalidDataException("Main header marker after tile data.");
                    }

                    HeaderParser.Read(marker, _input.Segment(), _main, _components.Length);
                }
            }

            if (!end)
            {
                throw new InvalidDataException("JPEG 2000 is missing EOC.");
            }

            _input.End();
            ReconstructTiles();
            return new Jpeg2000Image(_x0, _y0, _x1 - _x0, _y1 - _y0, _components, _info);
        }

        private void ReconstructTiles()
        {
            foreach (Tile? tile in _tiles)
            {
                if (tile is null || tile.PacketIndex != tile.Packets.Length)
                {
                    throw new InvalidDataException("JPEG 2000 is missing tile packets.");
                }

                foreach (TileComponent component in tile.Components)
                {
                    InverseWavelet.Reconstruct(component, _budget);
                }

                TileAssembler.Copy(tile, _components, _budget);
            }
        }

        private void Initialize(CodestreamSize size)
        {
            _x0 = size.X0;
            _y0 = size.Y0;
            _x1 = size.X1;
            _y1 = size.Y1;
            _tileX = size.TileX;
            _tileY = size.TileY;
            _tileWidth = size.TileWidth;
            _tileHeight = size.TileHeight;
            _across = size.Across;
            _budget.Reserve(size.TileCount * 8L);
            _tiles = new Tile?[size.TileCount];
            _components = new Jpeg2000Component[size.Components.Length];
            for (int c = 0; c < _components.Length; c++)
            {
                _components[c] = size.Components[c].Allocate(_budget);
            }
        }

        private void ReadTile(int start)
        {
            ByteReader? header = _input.Segment();
            int index = header.U16();
            uint size = header.U32();
            int part = header.U8();
            int parts = header.U8();
            header.End();
            if (index >= _tiles.Length || (parts != 0 && part >= parts))
            {
                throw new InvalidDataException("Invalid JPEG 2000 tile-part index.");
            }

            long end = size == 0 ? _input.Data.Length - 2 : start + (long)size;
            if (end > _input.Data.Length || end < _input.Position + 2)
            {
                throw new InvalidDataException("Invalid JPEG 2000 tile-part length.");
            }

            Tile? tile = _tiles[index];
            if (part != (tile?.NextPart ?? 0))
            {
                throw new InvalidDataException("Out-of-order JPEG 2000 tile-parts.");
            }

            HeaderSettings? settings = tile?.Settings ?? _main.Clone();
            ReadTileHeader(settings, tile is not null, end);
            if (tile is null)
            {
                _tiles[index] = tile = CreateTile(index, settings);
            }

            tile.NextPart++;
            ByteReader? packets = new ByteReader(_input.Take((int)(end - _input.Position)));
            PacketDecoder.Read(packets, tile, _budget);
        }

        private void ReadTileHeader(HeaderSettings settings, bool continuation, long end)
        {
            while (_input.Position < end)
            {
                int marker = _input.Marker();
                if (marker == 0xff93)
                {
                    return;
                }

                if (continuation && marker != 0xff58 && marker != 0xff64)
                {
                    throw new NotSupportedException("JPEG 2000 coding changes across tile-parts.");
                }

                HeaderParser.Read(marker, _input.Segment(), settings, _components.Length);
                if (_input.Position > end)
                {
                    throw new InvalidDataException("JPEG 2000 tile header exceeds its tile-part.");
                }
            }

            throw new InvalidDataException("JPEG 2000 tile-part is missing SOD.");
        }

        private Tile CreateTile(int index, HeaderSettings settings)
        {
            long left = _tileX + ((long)(index % _across) * _tileWidth);
            long top = _tileY + ((long)(index / _across) * _tileHeight);
            int x0 = (int)Math.Max(_x0, left);
            int y0 = (int)Math.Max(_y0, top);
            int x1 = (int)Math.Min(_x1, left + _tileWidth);
            int y1 = (int)Math.Min(_y1, top + _tileHeight);
            return TileBuilder.Build(_components, settings, x0, y0, x1, y1, _budget);
        }
    }
}
