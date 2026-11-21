// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Dts
{
    internal class DtsChannelCoding
    {
        public int ActiveBands { get; set; }
        public int VectorStart { get; set; }
        public int JointSource { get; set; }
        public int TransientCodebook { get; set; }
        public int ScaleCodebook { get; set; }
        public int AllocationCodebook { get; set; }
        public int JointCodebook { get; set; }
        public int[] Selectors { get; } = new int[27];
        public double[] Adjustments { get; } = new double[27];
        public int[] Predictors { get; } = new int[32];
        public int[] Allocations { get; } = new int[32];
        public int[] Transients { get; } = new int[32];
        public double[] BeforeScales { get; } = new double[32];
        public double[] AfterScales { get; } = new double[32];
        public double[] JointScales { get; } = new double[32];
        public int[] VectorIndexes { get; } = new int[32];
    }
}
