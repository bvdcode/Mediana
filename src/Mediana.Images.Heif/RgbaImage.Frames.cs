// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Images.Heif
{
    public partial class RgbaImage
    {
        /// <summary>Copies RGBA8 into an owned frame, retaining CICP and ICC without changing color.
        /// Pixel aspect must be supplied explicitly; HEIF transforms have already been applied.</summary>
        public VideoFrame ToVideoFrame(MediaBufferAllocator allocator, SampleAspectRatio sampleAspectRatio,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(allocator);
            cancellationToken.ThrowIfCancellationRequested();
            return RgbaFrame.CopyFrom(Pixels.Span, GetVideoFormat(sampleAspectRatio), allocator, cancellationToken);
        }

        internal VideoFormat GetVideoFormat(SampleAspectRatio sampleAspectRatio) => new(Width, Height, VideoPixelFormat.Rgba32,
            new VideoColorInfo(CicpColorimetry.GetPrimaries(ColorInformation.ColorPrimaries),
                CicpColorimetry.GetTransfer(ColorInformation.TransferCharacteristics), MatrixCoefficients.Rgb, ColorRange.Full),
            sampleAspectRatio, ColorInformation.IccDescription);
    }
}
