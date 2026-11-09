// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;

namespace Mediana.OperationComparison
{
    internal static class ComparisonApplication
    {
        internal static Task RunAsync(string[] arguments)
        {
            if (arguments.Length is >= 6 and <= 8 && arguments[0] == "h264-encode")
            {
                H264EntropyCoding entropy = H264EntropyCoding.Cavlc;
                if (arguments.Length >= 7)
                {
                    entropy = arguments[6] switch
                    {
                        "cavlc" => H264EntropyCoding.Cavlc,
                        "cabac" => H264EntropyCoding.Cabac,
                        _ => throw new ArgumentException("Expected cavlc or cabac entropy coding.")
                    };
                }
                H264MacroblockAnalysis analysis = H264MacroblockAnalysis.Fast;
                if (arguments.Length == 8)
                {
                    analysis = arguments[7] switch
                    {
                        "fast" => H264MacroblockAnalysis.Fast,
                        "rd" => H264MacroblockAnalysis.RateDistortion,
                        _ => throw new ArgumentException("Expected fast or rd macroblock analysis.")
                    };
                }
                return H264EncodingComparison.RunAsync(arguments[1], arguments[2], int.Parse(arguments[3], System.Globalization.CultureInfo.InvariantCulture),
                    int.Parse(arguments[4], System.Globalization.CultureInfo.InvariantCulture),
                    int.Parse(arguments[5], System.Globalization.CultureInfo.InvariantCulture), entropy, analysis);
            }
            if (arguments.Length != 3)
            {
                throw new ArgumentException("Expected operation, input and new output path.");
            }
            return arguments[0] switch
            {
                "video" => VideoComparison.RunAsync(arguments[1], arguments[2]),
                "video-yuv" => VideoComparison.RunAsync(arguments[1], arguments[2], true, true),
                "video-pooled" => VideoComparison.RunAsync(arguments[1], arguments[2], true),
                "ogg" => PacketComparison.RunAsync(arguments[1], arguments[2]),
                "xlsx" => SpreadsheetComparison.RunAsync(arguments[1], arguments[2]),
                "webp" => AnimationComparison.DecodeWebpAsync(arguments[1], arguments[2]),
                "tgs" => AnimationComparison.RenderTgsAsync(arguments[1], arguments[2]),
                _ => throw new ArgumentException("Unknown comparison operation.")
            };
        }
    }
}
