// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;

namespace Mediana.Images.Heif.Hevc
{
    internal readonly struct SaoParameters(int type, int band, int edge, int first, int second, int third, int fourth)
    {
        public int Type { get; } = type;
        public int Band { get; } = band;
        public int Edge { get; } = edge;

        public int Offset(int category)
        {
            switch (category)
            {
                case 0:
                    return 0;
                case 1:
                    return first;
                case 2:
                    return second;
                case 3:
                    return third;
                case 4:
                    return fourth;
                default:
                    throw new ArgumentOutOfRangeException(nameof(category));
            }
        }

        public static SaoParameters Read(IntraCabacReader reader, int component, int depth, SaoParameters chroma)
        {
            int type = component == 2 ? chroma.Type : reader.Read(CabacElement.SaoType) ? 1 + reader.Bits(1) : 0;
            if (type == 0)
            {
                return default;
            }
            Span<int> offsets = stackalloc int[4];
            int maximum = (1 << (depth - 5)) - 1;
            for (int i = 0; i < 4; i++)
            {
                offsets[i] = reader.Unary(maximum);
            }
            int band = 0;
            int edge = 0;
            if (type == 1)
            {
                for (int i = 0; i < 4; i++)
                {
                    if ((offsets[i] != 0) && reader.Bypass())
                    {
                        offsets[i] = -offsets[i];
                    }
                }
                band = reader.Bits(5);
            }
            else
            {
                offsets[2] = -offsets[2];
                offsets[3] = -offsets[3];
                edge = component == 2 ? chroma.Edge : reader.Bits(2);
            }

            return new SaoParameters(type, band, edge, offsets[0], offsets[1], offsets[2], offsets[3]);
        }
    }
}
