// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Globalization;
using Mediana.Core.Metadata;
using Mediana.Core.Timing;

namespace Mediana.Cli
{
    internal static class ImageInfoFormatter
    {
        internal static async Task WriteAsync(ImageInfo image, TextWriter output)
        {
            string unit = image.DimensionUnit switch
            {
                ImageDimensionUnit.Pixels => "pixels",
                ImageDimensionUnit.Points => "points",
                _ => throw new ArgumentOutOfRangeException(nameof(image))
            };
            await output.WriteLineAsync($"Image: {image.FormatId}; {Number(image.Width)}x{Number(image.Height)} {unit}.");
            if (image.PageCount is int pages)
            {
                await output.WriteLineAsync($"Pages: {pages}; dimensions describe the first page.");
            }
            if (image.Duration is MediaDuration duration)
            {
                double seconds = (double)duration.Value * duration.TimeBase.Numerator / duration.TimeBase.Denominator;
                await output.WriteLineAsync($"Declared duration: {Number(seconds)} seconds.");
            }
            if (image.FrameRate is double rate)
            {
                await output.WriteLineAsync($"Frame rate: {Number(rate)} fps.");
            }
        }

        private static string Number(double value) => value.ToString("0.#########", CultureInfo.InvariantCulture);
    }
}
