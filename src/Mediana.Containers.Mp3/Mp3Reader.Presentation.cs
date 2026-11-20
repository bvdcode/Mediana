// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Globalization;
using Mediana.Core.Timing;

namespace Mediana.Containers.Mp3
{
    public partial class Mp3Reader
    {
        /// <summary>Returns an explicit ID3 iTunSMPB selection on the coded sample timeline, when present and valid.
        /// Does not guess encoder delay. Unknown or malformed text tags remain ordinary metadata.</summary>
        public MediaTimeRange? GetPresentationRange(int streamIndex, CancellationToken cancellationToken = default)
        {
            RequireActive();
            if (streamIndex != 0) { throw new ArgumentOutOfRangeException(nameof(streamIndex)); }
            cancellationToken.ThrowIfCancellationRequested();
            Id3TextFrame? tag = TextFrames.FirstOrDefault(frame => frame.FrameId == "TXXX" && frame.Description == "iTunSMPB");
            if (tag is null || tag.Values.Count != 1) { return null; }
            string[] values = tag.Values[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (values.Length < 4 || !long.TryParse(values[1], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out long start)
                || !long.TryParse(values[2], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out long padding)
                || !long.TryParse(values[3], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out long samples)
                || start < 0 || padding < 0 || samples <= 0 || start > long.MaxValue - samples || start + samples > long.MaxValue - padding)
            { return null; }
            if (VbrInfo?.FrameCount is uint count && start + samples + padding != (long)count * FirstFrame.SamplesPerFrame)
            { throw new InvalidDataException("The MP3 presentation selection disagrees with its Xing frame count."); }
            TimeBase clock = new(1, FirstFrame.SampleRate);
            return new(new(start, clock), new(start + samples, clock));
        }
    }
}
