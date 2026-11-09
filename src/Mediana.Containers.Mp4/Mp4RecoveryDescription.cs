// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Mp4
{
    /// <summary>A constant roll or pre-roll declaration for every encoded sample of a fragment track.
    /// Distances count encoded samples, not decoded audio samples or video frames.</summary>
    public class Mp4RecoveryDescription
    {
        public Mp4RecoveryDescription(Mp4RecoveryKind kind, short distance)
        {
            GroupingType = kind switch
            {
                Mp4RecoveryKind.Roll => "roll",
                Mp4RecoveryKind.PreRoll => "prol",
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };
            ArgumentOutOfRangeException.ThrowIfZero(distance);
            Kind = kind;
            Distance = distance;
        }

        public Mp4RecoveryKind Kind { get; }
        /// <summary>Negative values require preceding encoded samples; positive values recover after the marked sample.</summary>
        public short Distance { get; }
        internal string GroupingType { get; }
    }
}
