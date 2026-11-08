// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Metadata;
using Mediana.Formats.Audio;

namespace Mediana.Containers.Matroska
{
    internal class MatroskaAc3Programme(string codec, MatroskaAudioSettings audio)
    {
        private readonly List<Eac3FrameMetadata> opening = [];
        private Ac3FrameHeader? first, main;
        private int dependents, expectedDependents = -1;
        public AudioStreamMetadata? Metadata { get; private set; }

        public bool Observe(Ac3FrameHeader header)
        {
            if (header.IsEnhanced != (codec == Ac3FrameHeader.EnhancedCodecId) || audio.SamplingFrequency != header.SampleRate)
            { throw new InvalidDataException("The Matroska AC-3 codec or sampling frequency disagrees with its syncframe."); }
            if (header.IsEnhanced && header.StreamType == 1)
            {
                if (main is null || main.StreamType != 0 || header.SubstreamId != dependents ||
                    header.SampleRate != main.SampleRate || header.SamplesPerFrame != main.SamplesPerFrame)
                { throw new InvalidDataException("The Matroska dependent E-AC-3 frame is not ordered and synchronized with its main frame."); }
                dependents++;
            }
            else
            {
                if (header.SubstreamId != 0)
                { throw new NotSupportedException("Multiple independent Matroska E-AC-3 programmes require explicit presentation selection."); }
                if (first is not null && (header.ChannelMode != first.ChannelMode || header.HasLfe != first.HasLfe || header.StreamType != first.StreamType))
                { throw new NotSupportedException("Matroska AC-3 sampling format changes require a new track."); }
                if (main is not null) { FinishFrameSet(); }
                first ??= header; main = header; dependents = 0;
                if (!header.IsEnhanced && Metadata is null)
                { SetMetadata(header, header.GetChannelOrder()); }
            }
            return header.IsEnhanced && Metadata is null;
        }

        public void AddOpening(Eac3FrameMetadata metadata) => opening.Add(metadata);
        public void Complete() { if (main is not null) { FinishFrameSet(); } }

        private void FinishFrameSet()
        {
            if (expectedDependents < 0) { expectedDependents = dependents; }
            else if (dependents != expectedDependents)
            { throw new InvalidDataException("The Matroska E-AC-3 dependent substream count changed or the final frame set is incomplete."); }
            if (Metadata is null)
            { SetMetadata(first!, Eac3ChannelMap.GetProgramChannelOrder(opening.ToArray())); opening.Clear(); }
        }

        private void SetMetadata(Ac3FrameHeader header, string[] channels)
        {
            if (audio.Channels != (ulong)channels.Length)
            { throw new InvalidDataException("The Matroska audio channel count disagrees with its opening AC-3 programme."); }
            Metadata = new(header.SampleRate, channels.Length, channelOrder: channels)
            { CodecProfile = new(header.IsEnhanced ? "E-AC-3" : "AC-3") };
        }
    }
}
