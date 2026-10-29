// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Images.Heif.Hevc;

namespace Mediana.Images.Heif.Container
{
    internal readonly struct ColorEncoding(int primaries, int transfer, int matrix, bool fullRange)
    {
        public int Primaries { get; } = primaries;
        public int Transfer { get; } = transfer;
        public int Matrix { get; } = matrix;
        public bool FullRange { get; } = fullRange;

        public static ColorEncoding FromVideo(VideoInformation video) => new ColorEncoding(
            video.ColourPrimaries, video.TransferCharacteristics, video.MatrixCoefficients, video.FullRange);
    }
}
