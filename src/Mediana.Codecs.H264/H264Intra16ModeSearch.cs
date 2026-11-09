// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal static class H264Intra16ModeSearch
    {
        public static int Select(H264SourceMacroblock source, H264Picture target, int x, int y, Span<byte> prediction)
        {
            int selected = 2, minimum = int.MaxValue;
            for (int mode = 0; mode < 4; mode++)
            {
                bool available = mode switch
                {
                    0 => target.IsInCurrentSlice(x, y - 1),
                    1 => target.IsInCurrentSlice(x - 1, y),
                    2 => true,
                    3 => target.IsInCurrentSlice(x, y - 1) && target.IsInCurrentSlice(x - 1, y) && target.IsInCurrentSlice(x - 1, y - 1),
                    _ => throw new ArgumentOutOfRangeException(nameof(mode))
                };
                if (!available) { continue; }
                target.PredictMacroblock(0, x, y, mode, prediction);
                int difference = 0;
                for (int index = 0; index < 256; index++) { difference += Math.Abs(source.Luma[index] - prediction[index]); }
                if (difference < minimum) { minimum = difference; selected = mode; }
            }
            return selected;
        }
    }
}
