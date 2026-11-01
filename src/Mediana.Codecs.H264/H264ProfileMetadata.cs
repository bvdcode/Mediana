// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Metadata;

namespace Mediana.Codecs.H264
{
    internal static class H264ProfileMetadata
    {
        public static CodecProfileInfo Read(int profile, int constraints, int level)
        {
            string name = profile switch
            {
                44 => "CAVLC 4:4:4 Intra",
                66 when (constraints & 0x40) != 0 => "Constrained Baseline",
                66 => "Baseline",
                77 => "Main",
                83 => "Scalable Baseline",
                86 => "Scalable High",
                88 => "Extended",
                100 when (constraints & 0x0c) == 0x0c => "Constrained High",
                100 when (constraints & 0x08) != 0 => "Progressive High",
                100 => "High",
                110 when (constraints & 0x10) != 0 => "High 10 Intra",
                110 when (constraints & 0x08) != 0 => "Progressive High 10",
                110 => "High 10",
                118 => "Multiview High",
                122 when (constraints & 0x10) != 0 => "High 4:2:2 Intra",
                122 => "High 4:2:2",
                128 => "Stereo High",
                134 => "MFC High",
                135 => "MFC Depth High",
                138 => "Multiview Depth High",
                139 => "Enhanced Multiview Depth High",
                244 when (constraints & 0x10) != 0 => "High 4:4:4 Intra",
                244 => "High 4:4:4 Predictive",
                _ => throw new NotSupportedException("The H.264 profile has no metadata description.")
            };
            string? levelName = level switch
            {
                9 when profile is not (66 or 77 or 88) => "1b",
                10 => "1",
                11 when profile is 66 or 77 or 88 && (constraints & 0x10) != 0 => "1b",
                11 => "1.1",
                12 => "1.2",
                13 => "1.3",
                20 => "2",
                21 => "2.1",
                22 => "2.2",
                30 => "3",
                31 => "3.1",
                32 => "3.2",
                40 => "4",
                41 => "4.1",
                42 => "4.2",
                50 => "5",
                51 => "5.1",
                52 => "5.2",
                60 => "6",
                61 => "6.1",
                62 => "6.2",
                _ => null
            };
            return new(name, levelName);
        }
    }
}
