// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Video;

namespace Mediana.Cli
{
    internal static class VideoColorDescriptionFormatter
    {
        public static async Task WriteAsync(VideoColorDescription description, TextWriter output)
        {
            switch (description)
            {
                case CicpColorDescription cicp:
                    string range = cicp.FullRange switch
                    {
                        true => "full",
                        false => "limited",
                        null => "not declared"
                    };
                    await output.WriteLineAsync($"  Source color: CICP primaries {cicp.Primaries}; transfer {cicp.Transfer}; matrix {cicp.Matrix}; range {range}.");
                    break;
                case IccColorDescription icc:
                    string restriction = icc.Restricted ? "restricted" : "unrestricted";
                    await output.WriteLineAsync($"  Source color: {restriction} ICC profile; {icc.Profile.Length} bytes.");
                    break;
                default:
                    await output.WriteLineAsync("  Source color: custom description.");
                    break;
            }
        }
    }
}
