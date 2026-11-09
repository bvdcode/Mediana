// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal readonly record struct H264CabacCostLimit(double Distortion, double Lambda, double Maximum, bool Strict)
        : IH264CabacCostLimit<H264CabacCostWriter>
    {
        public bool Reached(H264CabacCostWriter writer)
        {
            double cost = Distortion + Lambda * writer.Cost;
            return Strict ? cost > Maximum : cost >= Maximum;
        }
    }
}
