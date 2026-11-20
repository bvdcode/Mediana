// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Core.Video
{
    /// <summary>Row organization and temporal field order of a video surface.</summary>
    public enum VideoScanMode
    {
        Progressive,
        /// <summary>Interleaved field rows, with the top field presented first.</summary>
        TopFieldFirst,
        /// <summary>Interleaved field rows, with the bottom field presented first.</summary>
        BottomFieldFirst,
        /// <summary>Only top-field rows are stored. The stored height is the field's sample height.</summary>
        TopField,
        /// <summary>Only bottom-field rows are stored. The stored height is the field's sample height.</summary>
        BottomField
    }
}
