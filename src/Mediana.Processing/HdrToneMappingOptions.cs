// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Processing
{
    /// <summary>Explicit HDR display interpretation for Hable tone mapping to sRGB.
    /// Luminance is measured in cd/m². The source peak normalizes the highlight curve;
    /// for HLG it also defines the reference display and its system gamma.</summary>
    public record HdrToneMappingOptions
    {
        public double SourcePeakLuminance { get; init; } = 1000;
        public double TargetPeakLuminance { get; init; } = 100;

        internal void Validate()
        {
            if (!double.IsFinite(SourcePeakLuminance) || SourcePeakLuminance <= 0 || SourcePeakLuminance > 10000)
            { throw new ArgumentOutOfRangeException(nameof(SourcePeakLuminance)); }
            if (!double.IsFinite(TargetPeakLuminance) || TargetPeakLuminance <= 0 || TargetPeakLuminance > SourcePeakLuminance ||
                !double.IsFinite(1 / TargetPeakLuminance) || SourcePeakLuminance / TargetPeakLuminance > double.MaxValue / 4)
            { throw new ArgumentOutOfRangeException(nameof(TargetPeakLuminance)); }
        }
    }
}
