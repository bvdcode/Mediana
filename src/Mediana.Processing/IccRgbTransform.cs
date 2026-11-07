// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using System.Runtime.CompilerServices;
using Mediana.Core.Video;

namespace Mediana.Processing
{
    internal class IccRgbTransform : IRgbaPixelTransform
    {
        private static readonly ConditionalWeakTable<IccColorDescription, IccRgbTransform> Cache = new();
        private static readonly byte[] SrgbEncoding = CreateEncoding();
        private readonly Vector3[] red = new Vector3[256];
        private readonly Vector3[] green = new Vector3[256];
        private readonly Vector3[] blue = new Vector3[256];
        private readonly bool gray;

        private IccRgbTransform(IccColorDescription description, CancellationToken token)
        {
            IccMatrixProfile profile = new(description.Profile, token);
            gray = profile.Gray;
            RgbMatrix matrix = RgbPrimariesTransform.PcsD50ToSrgb.Multiply(profile.Matrix);
            for (int sample = 0; sample < 256; sample++)
            {
                token.ThrowIfCancellationRequested();
                red[sample] = new((float)(matrix.M11 * profile.Red[sample]), (float)(matrix.M21 * profile.Red[sample]), (float)(matrix.M31 * profile.Red[sample]));
                green[sample] = new((float)(matrix.M12 * profile.Green[sample]), (float)(matrix.M22 * profile.Green[sample]), (float)(matrix.M32 * profile.Green[sample]));
                blue[sample] = new((float)(matrix.M13 * profile.Blue[sample]), (float)(matrix.M23 * profile.Blue[sample]), (float)(matrix.M33 * profile.Blue[sample]));
            }
        }

        internal static IccRgbTransform Get(IccColorDescription description, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return Cache.GetValue(description, profile => new(profile, token));
        }

        public void WritePixel(Span<byte> destination, ReadOnlySpan<byte> source)
        {
            if (gray && (source[0] != source[1] || source[0] != source[2]))
            {
                throw new InvalidDataException("ICC grayscale profile requires replicated gray samples.");
            }
            Vector3 linear = red[source[0]] + green[source[1]] + blue[source[2]];
            destination[0] = Encode(linear.X);
            destination[1] = Encode(linear.Y);
            destination[2] = Encode(linear.Z);
            destination[3] = source[3];
        }

        private static byte Encode(float linear) => SrgbEncoding[(int)(Math.Clamp(linear, 0, 1) * (SrgbEncoding.Length - 1) + 0.5f)];

        private static byte[] CreateEncoding()
        {
            byte[] values = new byte[ushort.MaxValue + 1];
            for (int i = 0; i < values.Length; i++) { values[i] = SdrTransfer.EncodeSrgb((double)i / ushort.MaxValue); }
            return values;
        }
    }
}
