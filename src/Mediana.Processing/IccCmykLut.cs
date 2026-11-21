// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Processing
{
    internal class IccCmykLut(IccClut4 clut, IccToneCurve[] outputCurves, IccToneCurve[]? middleCurves,
        RgbMatrix matrix, Vector3 offset, IccPcsEncoding encoding)
    {
        internal Vector3 Evaluate(ReadOnlySpan<byte> source)
        {
            Vector3 value = clut.Evaluate(source);
            double x = value.X, y = value.Y, z = value.Z;
            if (middleCurves is not null)
            {
                x = middleCurves[0].Evaluate(x); y = middleCurves[1].Evaluate(y); z = middleCurves[2].Evaluate(z);
                (x, y, z) = matrix.Transform(x, y, z);
                x += offset.X; y += offset.Y; z += offset.Z;
            }
            x = outputCurves[0].Evaluate(x); y = outputCurves[1].Evaluate(y); z = outputCurves[2].Evaluate(z);
            switch (encoding)
            {
                case IccPcsEncoding.Xyz:
                    return new((float)(x * 65535 / 32768), (float)(y * 65535 / 32768), (float)(z * 65535 / 32768));
                case IccPcsEncoding.LegacyLab:
                    x = Math.Min(1, x * 65535 / 65280); y = Math.Min(1, y * 65535 / 65280); z = Math.Min(1, z * 65535 / 65280);
                    break;
                case IccPcsEncoding.Lab: break;
                default: throw new InvalidOperationException("Invalid ICC connection-space encoding.");
            }
            double fy = (x * 100 + 16) / 116, fx = fy + (y * 255 - 128) / 500, fz = fy - (z * 255 - 128) / 200;
            return new((float)(LabInverse(fx) * .9642), (float)LabInverse(fy), (float)(LabInverse(fz) * .8249));
        }

        private static double LabInverse(double value) => value > 6.0 / 29 ? value * value * value : (value - 4.0 / 29) * 108 / 841;
    }
}
