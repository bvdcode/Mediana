// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.OperationComparison
{
    internal static class ComparisonApplication
    {
        internal static Task RunAsync(string[] arguments)
        {
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
