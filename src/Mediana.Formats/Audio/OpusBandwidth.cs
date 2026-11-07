// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Formats.Audio
{
    /// <summary>The encoded signal bandwidth; the Opus media clock remains 48 kHz.</summary>
    public enum OpusBandwidth
    {
        Narrowband,
        Mediumband,
        Wideband,
        SuperWideband,
        Fullband
    }
}
