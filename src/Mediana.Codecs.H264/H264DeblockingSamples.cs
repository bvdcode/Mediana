// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.Intrinsics;

namespace Mediana.Codecs.H264
{
    internal struct H264DeblockingSamples
    {
        public Vector128<short> P3 { get; set; }
        public Vector128<short> P2 { get; set; }
        public Vector128<short> P1 { get; set; }
        public Vector128<short> P0 { get; set; }
        public Vector128<short> Q0 { get; set; }
        public Vector128<short> Q1 { get; set; }
        public Vector128<short> Q2 { get; set; }
        public Vector128<short> Q3 { get; set; }
    }
}
