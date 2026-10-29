// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Images.Heif
{
    public partial class RgbaImage
    {
        /// <summary>Copies RGBA8 into an owned frame without changing color. ICC conversion is unsupported.
        /// Pixel aspect must be supplied explicitly; HEIF transforms have already been applied.</summary>
        public VideoFrame ToVideoFrame(MediaBufferAllocator allocator, SampleAspectRatio sampleAspectRatio,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(allocator);
            cancellationToken.ThrowIfCancellationRequested();
            if (!ColorInformation.IccProfile.IsEmpty)
            {
                throw new NotSupportedException("ICC-profiled HEIC images require an explicit color transform.");
            }
            VideoColorInfo color = new(CicpColorimetry.GetPrimaries(ColorInformation.ColorPrimaries),
                CicpColorimetry.GetTransfer(ColorInformation.TransferCharacteristics), MatrixCoefficients.Rgb, ColorRange.Full);
            return RgbaFrame.CopyFrom(Pixels.Span, Width, Height, color, allocator, sampleAspectRatio, cancellationToken);
        }
    }
}
