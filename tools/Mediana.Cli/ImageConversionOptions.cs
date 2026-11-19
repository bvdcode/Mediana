// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>


namespace Mediana.Cli
{
    internal record ImageConversionOptions(ImageOutputSettings Encoding, bool Animate, long TimeMs, int Page)
    {
        public bool IsSpecified => Encoding.Compression is not null || Encoding.Quality is not null || Animate || TimeMs != 0 || Page != 1;
        public ConversionRequest Create(string output)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(TimeMs);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Page);
            if (Animate)
            {
                if (Page != 1) { throw new ArgumentException("Animation output does not select document pages."); }
                return new AnimationConversionRequest(ImageOutputEncoders.SequenceForPath(output, Encoding), TimeMs);
            }
            return new ImageConversionRequest(ImageOutputEncoders.ForPath(output, Encoding), TimeMs, Page);
        }
    }
}
