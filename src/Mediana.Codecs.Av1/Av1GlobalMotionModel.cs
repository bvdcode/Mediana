// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal readonly record struct Av1GlobalMotionModel(Av1GlobalMotionType Type, Av1WarpParameters Parameters, bool InvalidWarp = false)
    {
        private const int WarpPrecision = 16;
        private const int IdentityScale = 1 << WarpPrecision;

        internal bool Affine => Type is Av1GlobalMotionType.RotationZoom or Av1GlobalMotionType.Affine;
        internal Av1WarpParameters Warp => Affine && !InvalidWarp ? Parameters : default;

        internal int Parameter(int index)
        {
            if (Type == Av1GlobalMotionType.Identity)
            {
                return index switch
                {
                    2 or 5 => IdentityScale,
                    0 or 1 or 3 or 4 => 0,
                    _ => throw new ArgumentOutOfRangeException(nameof(index))
                };
            }
            return index switch
            {
                0 => Parameters.X,
                1 => Parameters.Y,
                2 => Parameters.A,
                3 => Parameters.B,
                4 => Parameters.C,
                5 => Parameters.D,
                _ => throw new ArgumentOutOfRangeException(nameof(index))
            };
        }

        internal Av1MotionCandidate Motion(int row, int column, int width, int height, bool integer, bool highPrecision)
        {
            int y;
            int x;
            switch (Type)
            {
                case Av1GlobalMotionType.Identity:
                    return default;
                case Av1GlobalMotionType.Translation:
                    y = Parameters.X >> (WarpPrecision - 3);
                    x = Parameters.Y >> (WarpPrecision - 3);
                    break;
                case Av1GlobalMotionType.RotationZoom:
                case Av1GlobalMotionType.Affine:
                    int centerX = column * 4 + width * 2 - 1;
                    int centerY = row * 4 + height * 2 - 1;
                    long projectedX = (long)(Parameters.A - IdentityScale) * centerX + (long)Parameters.B * centerY + Parameters.X;
                    long projectedY = (long)Parameters.C * centerX + (long)(Parameters.D - IdentityScale) * centerY + Parameters.Y;
                    int shift = highPrecision ? WarpPrecision - 3 : WarpPrecision - 2;
                    int factor = highPrecision ? 1 : 2;
                    y = checked((int)Av1WarpEstimation.RoundSigned(projectedY, shift) * factor);
                    x = checked((int)Av1WarpEstimation.RoundSigned(projectedX, shift) * factor);
                    break;
                default:
                    throw new InvalidDataException("The AV1 global motion type is invalid.");
            }
            return new(Av1MotionPrecision.Lower(y, integer, highPrecision), Av1MotionPrecision.Lower(x, integer, highPrecision), 0);
        }

        internal static Av1GlobalMotionModel Read(ref Av1BitReader bits, bool highPrecision, Av1GlobalMotionModel previous)
        {
            if (!bits.Flag()) { return default; }
            Av1GlobalMotionType type = bits.Flag() ? Av1GlobalMotionType.RotationZoom
                : bits.Flag() ? Av1GlobalMotionType.Translation : Av1GlobalMotionType.Affine;
            Span<int> parameters = stackalloc int[6];
            parameters.Clear();
            parameters[2] = parameters[5] = IdentityScale;
            if (type is Av1GlobalMotionType.RotationZoom or Av1GlobalMotionType.Affine)
            {
                parameters[2] = ReadParameter(ref bits, type, 2, highPrecision, previous);
                parameters[3] = ReadParameter(ref bits, type, 3, highPrecision, previous);
                if (type == Av1GlobalMotionType.Affine)
                {
                    parameters[4] = ReadParameter(ref bits, type, 4, highPrecision, previous);
                    parameters[5] = ReadParameter(ref bits, type, 5, highPrecision, previous);
                }
                else
                {
                    parameters[4] = -parameters[3];
                    parameters[5] = parameters[2];
                }
            }
            parameters[0] = ReadParameter(ref bits, type, 0, highPrecision, previous);
            parameters[1] = ReadParameter(ref bits, type, 1, highPrecision, previous);
            Av1WarpParameters warp = Av1WarpParameters.Create(parameters[0], parameters[1], parameters[2], parameters[3], parameters[4], parameters[5]);
            if (warp.IsValid) { return new(type, warp); }
            return new(type, new(parameters[0], parameters[1], parameters[2], parameters[3], parameters[4], parameters[5]), true);
        }

        private static int ReadParameter(ref Av1BitReader bits, Av1GlobalMotionType type, int index, bool highPrecision, Av1GlobalMotionModel previous)
        {
            int absoluteBits = 12;
            int precisionBits = 15;
            if (index < 2)
            {
                if (type == Av1GlobalMotionType.Translation)
                {
                    absoluteBits = highPrecision ? 9 : 8;
                    precisionBits = highPrecision ? 3 : 2;
                }
                else { precisionBits = 6; }
            }
            int precisionDifference = WarpPrecision - precisionBits;
            int offset = index is 2 or 5 ? 1 << precisionBits : 0;
            int reference = (previous.Parameter(index) >> precisionDifference) - offset;
            int limit = 1 << absoluteBits;
            int value = Av1Subexponential.ReadSigned(ref bits, -limit, limit + 1, reference);
            return (value << precisionDifference) + (index is 2 or 5 ? IdentityScale : 0);
        }
    }
}
