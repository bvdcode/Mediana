// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    /// <summary>Controls the prediction modes considered by the H.264 encoder.</summary>
    public enum H264MacroblockAnalysis
    {
        /// <summary>Uses motion-compensated prediction for predictive pictures.</summary>
        Fast,
        /// <summary>Also compares selected Intra16 candidates by reconstruction error and CABAC rate.</summary>
        RateDistortion
    }
}
