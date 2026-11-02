// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Video;

namespace Mediana.Containers.Matroska
{
    /// <summary>Resolves specified native CICP, range and chroma siting over codec labels.</summary>
    public class MatroskaColorDescription(MatroskaColourSettings settings) : VideoColorDescription
    {
        public MatroskaColourSettings Settings { get; } = settings ?? throw new ArgumentNullException(nameof(settings));

        public override VideoColorInfo Resolve(VideoColorInfo codecColor)
        {
            ArgumentNullException.ThrowIfNull(codecColor);
            if (Settings.Primaries > byte.MaxValue || Settings.TransferCharacteristics > byte.MaxValue ||
                Settings.MatrixCoefficients > byte.MaxValue || Settings.Range > 2 ||
                Settings.ChromaSitingHorz > 2 || Settings.ChromaSitingVert > 2)
            {
                throw new NotSupportedException("The Matroska Colour description contains unsupported codes.");
            }
            CicpColorDescription cicp = new((int)Settings.Primaries, (int)Settings.TransferCharacteristics,
                (int)Settings.MatrixCoefficients);
            VideoColorInfo described = cicp.Resolve(codecColor);
            ColorRange range = Settings.Range switch
            {
                0 => codecColor.Range,
                1 => ColorRange.Limited,
                2 => ColorRange.Full,
                _ => throw new NotSupportedException("The Matroska color range is unsupported.")
            };
            (ulong horizontal, ulong vertical) = codecColor.ChromaLocation switch
            {
                ChromaLocation.Unspecified => (0UL, 0UL),
                ChromaLocation.Left => (1UL, 2UL),
                ChromaLocation.Center => (2UL, 2UL),
                ChromaLocation.TopLeft => (1UL, 1UL),
                ChromaLocation.Top => (2UL, 1UL),
                ChromaLocation.BottomLeft => (1UL, 3UL),
                ChromaLocation.Bottom => (2UL, 3UL),
                _ => throw new ArgumentOutOfRangeException(nameof(codecColor))
            };
            if (Settings.ChromaSitingHorz != 0)
            {
                horizontal = Settings.ChromaSitingHorz;
            }
            if (Settings.ChromaSitingVert != 0)
            {
                vertical = Settings.ChromaSitingVert;
            }
            ChromaLocation location = (horizontal, vertical) switch
            {
                (0, _) or (_, 0) => ChromaLocation.Unspecified,
                (1, 1) => ChromaLocation.TopLeft,
                (1, 2) => ChromaLocation.Left,
                (1, 3) => ChromaLocation.BottomLeft,
                (2, 1) => ChromaLocation.Top,
                (2, 2) => ChromaLocation.Center,
                (2, 3) => ChromaLocation.Bottom,
                _ => throw new InvalidOperationException("The resolved Matroska chroma siting is inconsistent.")
            };
            return new(Settings.Primaries == 2 ? codecColor.Primaries : described.Primaries,
                Settings.TransferCharacteristics == 2 ? codecColor.Transfer : described.Transfer,
                Settings.MatrixCoefficients == 2 ? codecColor.Matrix : described.Matrix, range, location);
        }
    }
}
