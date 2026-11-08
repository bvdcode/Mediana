// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal ref partial struct Av1TileDecoder
    {
        private const byte DecodedFlag = 1;
        private const byte SmoothFlag = 2;

        private bool SmoothNeighbors(int plane, int row, int column)
        {
            int sub = sequence.PlaneShift(plane);
            int planeRow = row >> sub;
            int planeColumn = column >> sub;
            return HasDecodedFlag(plane, planeColumn, planeRow - 1, SmoothFlag) ||
                HasDecodedFlag(plane, planeColumn - 1, planeRow, SmoothFlag);
        }

        private bool IsDecoded(int plane, int column, int row) => HasDecodedFlag(plane, column, row, DecodedFlag);

        private bool HasDecodedFlag(int plane, int column, int row, byte flag) => column >= 0 && row >= 0 &&
            column < sequence.PlaneColumns(plane) && row < sequence.PlaneRows(plane) &&
            (decoded[sequence.PlaneOffset(plane) / 16 + row * sequence.PlaneColumns(plane) + column] & flag) != 0;
    }
}
