// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Core.Video
{
    /// <summary>Original CICP codes. A null full-range flag leaves the codec's range unchanged.</summary>
    public class CicpColorDescription : VideoColorDescription
    {
        public CicpColorDescription(int primaries, int transfer, int matrix, bool? fullRange = null)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(primaries);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(primaries, ushort.MaxValue);
            ArgumentOutOfRangeException.ThrowIfNegative(transfer);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(transfer, ushort.MaxValue);
            ArgumentOutOfRangeException.ThrowIfNegative(matrix);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(matrix, ushort.MaxValue);
            Primaries = primaries;
            Transfer = transfer;
            Matrix = matrix;
            FullRange = fullRange;
        }

        public int Primaries { get; }
        public int Transfer { get; }
        public int Matrix { get; }
        public bool? FullRange { get; }

        public override VideoColorInfo Resolve(VideoColorInfo codecColor)
        {
            ArgumentNullException.ThrowIfNull(codecColor);
            if (Primaries > byte.MaxValue || Transfer > byte.MaxValue || Matrix > byte.MaxValue)
            {
                throw UnsupportedCodes();
            }
            ColorPrimaries primaries = CicpColorimetry.GetPrimaries(Primaries);
            TransferFunction transfer = CicpColorimetry.GetTransfer(Transfer);
            MatrixCoefficients matrix = CicpColorimetry.GetMatrix(Matrix);
            if ((primaries == ColorPrimaries.Unspecified && Primaries != 2) ||
                (transfer == TransferFunction.Unspecified && Transfer != 2) ||
                (matrix == MatrixCoefficients.Unspecified && Matrix != 2))
            {
                throw UnsupportedCodes();
            }
            ColorRange range = FullRange switch
            {
                true => ColorRange.Full,
                false => ColorRange.Limited,
                null => codecColor.Range
            };
            return new VideoColorInfo(primaries, transfer, matrix, range, codecColor.ChromaLocation);
        }

        private NotSupportedException UnsupportedCodes() =>
            new($"The source CICP description uses unsupported codes ({Primaries}/{Transfer}/{Matrix}).");
    }
}
