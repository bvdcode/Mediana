// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Mp3
{
    internal static class Mp3ScaleFactorBands
    {
        public const int SpectrumLength = 576;
        public const int LongBandCount = 22;
        public const int ShortBandCount = 13;

        public static ReadOnlySpan<int> Long(int sampleRate) => sampleRate switch
        {
            44100 => [0, 4, 8, 12, 16, 20, 24, 30, 36, 44, 52, 62, 74, 90, 110, 134, 162, 196, 238, 288, 342, 418, 576],
            48000 => [0, 4, 8, 12, 16, 20, 24, 30, 36, 42, 50, 60, 72, 88, 106, 128, 156, 190, 230, 276, 330, 384, 576],
            32000 => [0, 4, 8, 12, 16, 20, 24, 30, 36, 44, 54, 66, 82, 102, 126, 156, 194, 240, 296, 364, 448, 550, 576],
            22050 or 16000 or 11025 or 12000 => [0, 6, 12, 18, 24, 30, 36, 44, 54, 66, 80, 96, 116, 140, 168, 200, 238, 284, 336, 396, 464, 522, 576],
            24000 => [0, 6, 12, 18, 24, 30, 36, 44, 54, 66, 80, 96, 114, 136, 162, 194, 232, 278, 332, 394, 464, 540, 576],
            8000 => [0, 12, 24, 36, 48, 60, 72, 88, 108, 132, 160, 192, 232, 280, 336, 400, 476, 566, 568, 570, 572, 574, 576],
            _ => throw new InvalidDataException("The MP3 sample rate has no scalefactor bands.")
        };

        public static ReadOnlySpan<int> Short(int sampleRate) => sampleRate switch
        {
            44100 => [0, 4, 8, 12, 16, 22, 30, 40, 52, 66, 84, 106, 136, 192],
            48000 => [0, 4, 8, 12, 16, 22, 28, 38, 50, 64, 80, 100, 126, 192],
            32000 => [0, 4, 8, 12, 16, 22, 30, 42, 58, 78, 104, 138, 180, 192],
            22050 => [0, 4, 8, 12, 18, 24, 32, 42, 56, 74, 100, 132, 174, 192],
            24000 => [0, 4, 8, 12, 18, 26, 36, 48, 62, 80, 104, 136, 180, 192],
            16000 or 11025 or 12000 => [0, 4, 8, 12, 18, 26, 36, 48, 62, 80, 104, 134, 174, 192],
            8000 => [0, 8, 16, 24, 36, 52, 72, 96, 124, 160, 162, 164, 166, 192],
            _ => throw new InvalidDataException("The MP3 sample rate has no scalefactor bands.")
        };
    }
}
