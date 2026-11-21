// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using System.Runtime.CompilerServices;
using Mediana.Core.Video;

namespace Mediana.Processing
{
    internal class IccCmykTransform : IPackedPixelTransform
    {
        private static readonly ConditionalWeakTable<IccColorDescription, IccCmykTransform> Cache = new();
        private readonly IccCmykLut lut;
        private IccCmykTransform(IccColorDescription description, CancellationToken token)
        {
            IccProfile profile = new(description.Profile, token);
            lut = IccLutReader.Read(profile, token);
        }
        internal static IccCmykTransform Get(IccColorDescription description, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return Cache.GetValue(description, profile => new(profile, token));
        }
        public void WritePixel(Span<byte> destination, ReadOnlySpan<byte> source)
        {
            Vector3 xyz = lut.Evaluate(source);
            (double r, double g, double b) = RgbPrimariesTransform.PcsD50ToSrgb.Transform(xyz.X, xyz.Y, xyz.Z);
            destination[0] = IccSrgbEncoding.Encode((float)r); destination[1] = IccSrgbEncoding.Encode((float)g); destination[2] = IccSrgbEncoding.Encode((float)b);
            destination[3] = 255;
        }
    }
}
