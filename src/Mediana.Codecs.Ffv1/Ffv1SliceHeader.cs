// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Video;

namespace Mediana.Codecs.Ffv1
{
    internal readonly record struct Ffv1SliceHeader(int X, int Y, int Width, int Height, int LumaTable, int ChromaTable,
        VideoScanMode? Scan, SampleAspectRatio? Aspect)
    {
        public static Ffv1SliceHeader Read(scoped ref Ffv1RangeReader reader, Ffv1SliceConfiguration configuration)
        {
            Span<byte> context = stackalloc byte[Ffv1FrameModel.StateBytes];
            context.Fill(128);
            int x = reader.ReadSymbol(context);
            int y = reader.ReadSymbol(context);
            int width = checked(reader.ReadSymbol(context) + 1);
            int height = checked(reader.ReadSymbol(context) + 1);
            int luma = reader.ReadSymbol(context);
            int chroma = reader.ReadSymbol(context);
            if (x > configuration.Columns - width || y > configuration.Rows - height ||
                luma >= configuration.Quantizers.Length || chroma >= configuration.Quantizers.Length)
            {
                throw new InvalidDataException("Invalid FFV1 slice coordinates or context model.");
            }
            VideoScanMode? scan = reader.ReadSymbol(context) switch
            {
                0 => null,
                1 => VideoScanMode.TopFieldFirst,
                2 => VideoScanMode.BottomFieldFirst,
                3 => VideoScanMode.Progressive,
                _ => throw new InvalidDataException("Invalid FFV1 picture structure.")
            };
            int numerator = reader.ReadSymbol(context);
            int denominator = reader.ReadSymbol(context);
            if (denominator == 0) { throw new InvalidDataException("Invalid FFV1 sample aspect ratio."); }
            SampleAspectRatio? aspect = numerator == 0 ? null : new(numerator, denominator);
            return new(x, y, width, height, luma, chroma, scan, aspect);
        }
    }
}
