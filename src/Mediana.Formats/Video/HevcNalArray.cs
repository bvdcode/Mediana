// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Formats.Video
{
    /// <summary>A group of HEVC NAL units with the same type in a decoder configuration record.</summary>
    public class HevcNalArray
    {
        internal HevcNalArray(int type, bool complete, ReadOnlyMemory<byte>[] units)
        {
            Type = type;
            IsComplete = complete;
            Units = Array.AsReadOnly(units);
        }

        public int Type { get; }
        public bool IsComplete { get; }
        /// <summary>Encoded NAL units, including their two-byte headers.</summary>
        public IReadOnlyList<ReadOnlyMemory<byte>> Units { get; }
    }
}
