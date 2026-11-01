// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Video;
using Mediana.Processing;
using Microsoft.Extensions.Logging;

namespace Mediana.Cli
{
    internal static class PreviewColorPolicy
    {
        internal static VideoColorInfo Resolve(VideoFormat format, ILogger logger)
        {
            VideoColorInfo color = Resolve(format);
            VideoColorInfo declared = format.GetColorInterpretation();
            if (color != declared)
            {
                List<string> missing = [];
                if (declared.Primaries == ColorPrimaries.Unspecified)
                {
                    missing.Add("primaries");
                }
                if (declared.Transfer == TransferFunction.Unspecified)
                {
                    missing.Add("transfer");
                }
                if (declared.Matrix == MatrixCoefficients.Unspecified)
                {
                    missing.Add("matrix");
                }
                CliLog.PreviewColorAssumed(logger, string.Join(", ", missing), color.Primaries, color.Transfer, color.Matrix);
            }
            return color;
        }

        internal static VideoColorInfo Resolve(VideoFormat format) => SdrPreviewColorPolicy.Resolve(format);
    }
}
