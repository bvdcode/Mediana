// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Formats.Color
{
    /// <summary>Multiplicative conversion of unprofiled eight-bit ink coverage to device RGB.</summary>
    public static class DeviceCmyk
    {
        public static (byte Red, byte Green, byte Blue) ToRgb(byte cyan, byte magenta, byte yellow, byte black)
        {
            int white = 255 - black;
            return ((byte)(((255 - cyan) * white + 127) / 255),
                (byte)(((255 - magenta) * white + 127) / 255),
                (byte)(((255 - yellow) * white + 127) / 255));
        }
    }
}
