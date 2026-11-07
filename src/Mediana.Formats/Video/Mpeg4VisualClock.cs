// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Formats.Video
{
    /// <summary>Reconstructs MPEG-4 Visual presentation ticks in decoding order, including B pictures and group time codes.</summary>
    public class Mpeg4VisualClock
    {
        private long timeBase, previousTimeBase;
        public long LastReferenceTime { get; private set; }
        public long ReferenceInterval { get; private set; }
        public void ReadGroup(ReadOnlySpan<byte> payload)
        {
            Mpeg4VisualBitReader bits = new(payload);
            int hours = bits.Read(5), minutes = bits.Read(6); bits.Marker(); int seconds = bits.Read(6); bits.Skip(2);
            if (hours > 23 || minutes > 59 || seconds > 59) { throw new InvalidDataException("The MPEG-4 Visual group time code is invalid."); }
            timeBase = seconds + 60 * (minutes + 60L * hours);
        }
        public long Read(Mpeg4VopHeader header, Mpeg4VisualConfiguration configuration)
        {
            if (header.Type == Mpeg4VopType.Bidirectional)
            { return checked((previousTimeBase + header.ModuloTimeBase) * configuration.TimeIncrementResolution + header.TimeIncrement); }
            previousTimeBase = timeBase; timeBase = checked(timeBase + header.ModuloTimeBase);
            long time = checked(timeBase * configuration.TimeIncrementResolution + header.TimeIncrement);
            ReferenceInterval = time - LastReferenceTime; LastReferenceTime = time; return time;
        }
    }
}
