// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Timing;

namespace Mediana.Streaming.Hls
{
    /// <summary>A frame-aligned presentation interval to encode independently on request.</summary>
    public readonly record struct HlsVideoWindow(int Number, MediaTimestamp Start, MediaDuration Duration)
    {
        public MediaTimestamp End
        {
            get
            {
                if (Start.TimeBase != Duration.TimeBase) { throw new InvalidOperationException("The HLS window requires one common clock."); }
                return new(checked(Start.Value + Duration.Value), Start.TimeBase);
            }
        }
    }
}
