// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Timing;

namespace Mediana.Codecs
{
    /// <summary>Allows an indexed video decoder to omit pictures used only for presentation preroll.</summary>
    public interface IVideoPresentationPrerollDecoder
    {
        /// <summary>Allows dropping pictures whose complete presentation interval ends by <paramref name="start"/>
        /// and that are unnecessary for decoding later pictures. Call after seeking, before sending packets with indexed
        /// presentation durations. A decoder may still emit preroll, which the caller must trim. Reset clears the hint.</summary>
        void SetPresentationStart(MediaTimestamp start);
    }
}
