// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Formats.Video
{
    /// <summary>One MPEG-4 Visual picture header, with its coded clock increment.</summary>
    public readonly record struct Mpeg4VopHeader(Mpeg4VopType Type, int ModuloTimeBase, int TimeIncrement, bool IsCoded,
        bool Rounding, int Quantizer, int IntraDcThreshold, int ForwardCode, int BackwardCode,
        bool TopFieldFirst, bool AlternateScan)
    {
        public static Mpeg4VopHeader Read(ref Mpeg4VisualBitReader bits, Mpeg4VisualConfiguration configuration)
        {
            Mpeg4VopType type = (Mpeg4VopType)bits.Read(2); int modulo = 0;
            while (bits.Flag()) { modulo = checked(modulo + 1); }
            bits.Marker(); int increment = bits.Read(configuration.TimeIncrementBits); bits.Marker();
            if (increment >= configuration.TimeIncrementResolution) { throw new InvalidDataException("The MPEG-4 Visual picture clock increment exceeds its resolution."); }
            bool coded = bits.Flag();
            if (!coded) { return new(type, modulo, increment, false, false, 0, 0, 0, 0, false, false); }
            if (configuration.NewPrediction)
            {
                int length = Math.Min(configuration.TimeIncrementBits + 3, 15); bits.Skip(length);
                if (bits.Flag()) { bits.Skip(length); } bits.Marker();
            }
            bool rounding = (type == Mpeg4VopType.Predictive || type == Mpeg4VopType.Sprite && configuration.SpriteMode == 2) && bits.Flag();
            if (configuration.ReducedResolution && type is Mpeg4VopType.Intra or Mpeg4VopType.Predictive && bits.Flag())
            { throw new NotSupportedException("Reduced-resolution MPEG-4 Visual pictures are not supported."); }
            ReadOnlySpan<byte> thresholds = [32,13,15,17,19,21,23,1]; int dc = thresholds[bits.Read(3)];
            bool top = false, alternate = false;
            if (configuration.Interlaced) { top = bits.Flag(); alternate = bits.Flag(); }
            if (type == Mpeg4VopType.Sprite) { throw new NotSupportedException("MPEG-4 Visual sprite pictures are not supported."); }
            int quantizer = bits.Read(configuration.QuantizerBits);
            if (quantizer == 0) { throw new InvalidDataException("The MPEG-4 Visual quantizer is zero."); }
            int forward = type != Mpeg4VopType.Intra ? bits.Read(3) : 1;
            int backward = type == Mpeg4VopType.Bidirectional ? bits.Read(3) : 1;
            if (forward == 0 || backward == 0) { throw new InvalidDataException("An MPEG-4 Visual motion-vector code is zero."); }
            return new(type, modulo, increment, true, rounding, quantizer, dc, forward, backward, top, alternate);
        }
    }
}
