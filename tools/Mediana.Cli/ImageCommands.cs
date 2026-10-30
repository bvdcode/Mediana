// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using ConsoleAppFramework;
using Mediana.Codecs;
using Mediana.Core.Video;

namespace Mediana.Cli
{
    internal class ImageCommands : MediaCommands
    {
        /// <summary>Decode a supported HEIC/HEIF still image to PNG.</summary>
        /// <param name="input">Existing HEIC/HEIF file.</param>
        /// <param name="output">New PNG file.</param>
        /// <param name="aspectWidth">Pixel aspect ratio numerator.</param>
        /// <param name="aspectHeight">Pixel aspect ratio denominator.</param>
        public Task<int> Heic([Argument] string input, [Argument] string output, int aspectWidth = 1, int aspectHeight = 1,
            CancellationToken cancellationToken = default) => RasterAsync(ImageEngineFactory.CreateHeic(new SampleAspectRatio(aspectWidth, aspectHeight)), input, output, cancellationToken);

        /// <summary>Decode a supported JPEG 2000 image to PNG.</summary>
        /// <param name="input">Existing JP2/J2K file.</param>
        /// <param name="output">New PNG file.</param>
        /// <param name="aspectWidth">Pixel aspect ratio numerator.</param>
        /// <param name="aspectHeight">Pixel aspect ratio denominator.</param>
        public Task<int> Jp2([Argument] string input, [Argument] string output, int aspectWidth = 1, int aspectHeight = 1,
            CancellationToken cancellationToken = default) => RasterAsync(ImageEngineFactory.CreateJpeg2000(new SampleAspectRatio(aspectWidth, aspectHeight)), input, output, cancellationToken);

        /// <summary>Decode the first supported JBIG2 page to PNG.</summary>
        /// <param name="input">Existing standalone JBIG2 file.</param>
        /// <param name="output">New PNG file.</param>
        /// <param name="aspectWidth">Pixel aspect ratio numerator.</param>
        /// <param name="aspectHeight">Pixel aspect ratio denominator.</param>
        public Task<int> Jbig2([Argument] string input, [Argument] string output, int aspectWidth = 1, int aspectHeight = 1,
            CancellationToken cancellationToken = default) => RasterAsync(ImageEngineFactory.CreateJbig2(new SampleAspectRatio(aspectWidth, aspectHeight)), input, output, cancellationToken);

        /// <summary>Rasterize an SVG document to PNG.</summary>
        /// <param name="input">Existing SVG file.</param>
        /// <param name="output">New PNG file.</param>
        /// <param name="width">Output width, preserving proportions.</param>
        public Task<int> Svg([Argument] string input, [Argument] string output, int width = 512,
            CancellationToken cancellationToken = default)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
            return ExecuteAsync(input, output, () => ImagePreviewCommand.SvgAsync(input, output, width, cancellationToken));
        }

        /// <summary>Render one supported TGS/Lottie frame to PNG.</summary>
        /// <param name="input">Existing TGS or Lottie JSON file.</param>
        /// <param name="output">New PNG file.</param>
        /// <param name="time">Elapsed animation time in seconds.</param>
        /// <param name="width">Output width, preserving proportions.</param>
        public Task<int> Tgs([Argument] string input, [Argument] string output, double time = 0, int width = 512,
            CancellationToken cancellationToken = default)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
            if (!double.IsFinite(time) || time < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(time), "Animation time must be finite and nonnegative.");
            }
            return ExecuteAsync(input, output, () => ImagePreviewCommand.TgsAsync(input, output, time, width, cancellationToken));
        }

        private static Task<int> RasterAsync(IImageDecoder decoder, string input, string output, CancellationToken token) =>
            ExecuteAsync(input, output, () => ImagePreviewCommand.RunAsync(decoder, input, output, cancellationToken: token));
    }
}
