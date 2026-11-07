// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Vp9
{
    internal static class Vp9Scan
    {
        internal static ReadOnlySpan<ushort> Get(int size, Vp9TransformType type) => (size, type) switch
        {
            (0, Vp9TransformType.DctDct or Vp9TransformType.AdstAdst) => Vp9Tables.Scan4,
            (0, Vp9TransformType.AdstDct) => Vp9Tables.RowScan4,
            (0, Vp9TransformType.DctAdst) => Vp9Tables.ColumnScan4,
            (1, Vp9TransformType.DctDct or Vp9TransformType.AdstAdst) => Vp9Tables.Scan8,
            (1, Vp9TransformType.AdstDct) => Vp9Tables.RowScan8,
            (1, Vp9TransformType.DctAdst) => Vp9Tables.ColumnScan8,
            (2, Vp9TransformType.DctDct or Vp9TransformType.AdstAdst) => Vp9Tables.Scan16,
            (2, Vp9TransformType.AdstDct) => Vp9Tables.RowScan16,
            (2, Vp9TransformType.DctAdst) => Vp9Tables.ColumnScan16,
            (3, Vp9TransformType.DctDct) => Vp9Tables.Scan32,
            _ => throw new InvalidDataException("Invalid VP9 coefficient scan.")
        };
    }
}
