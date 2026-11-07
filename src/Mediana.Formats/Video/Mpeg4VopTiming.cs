// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Formats.Video
{
    /// <summary>Picture clock syntax available before macroblock or coefficient decoding.</summary>
    public readonly record struct Mpeg4VopTiming(Mpeg4VopType Type, int ModuloTimeBase, int TimeIncrement, bool IsCoded)
    {
        public static Mpeg4VopTiming Read(ref Mpeg4VisualBitReader bits, Mpeg4VisualConfiguration configuration)
        {
            if (!TryRead(ref bits, configuration, out Mpeg4VopTiming result)) { throw new InvalidDataException("The MPEG-4 Visual picture clock is truncated."); }
            return result;
        }
        /// <summary>Returns false without consuming bits when a larger prefix is required. Invalid markers and clock values throw.</summary>
        public static bool TryRead(ref Mpeg4VisualBitReader bits, Mpeg4VisualConfiguration configuration, out Mpeg4VopTiming timing)
        {
            timing = default; Mpeg4VisualBitReader source = bits;
            if (source.RemainingBits < 3) { return false; }
            Mpeg4VopType type = (Mpeg4VopType)source.Read(2); int modulo = 0;
            while (source.Flag())
            { modulo = checked(modulo + 1); if (source.RemainingBits == 0) { return false; } }
            if (source.RemainingBits < configuration.TimeIncrementBits + 3) { return false; }
            source.Marker(); int increment = source.Read(configuration.TimeIncrementBits); source.Marker(); bool coded = source.Flag();
            if (increment >= configuration.TimeIncrementResolution) { throw new InvalidDataException("The MPEG-4 Visual picture clock increment exceeds its resolution."); }
            timing = new(type, modulo, increment, coded); bits = source; return true;
        }
    }
}
