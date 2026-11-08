// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Formats.Audio;

namespace Mediana.Codecs.Ac3
{
    internal class Eac3FrameSetAssembly
    {
        public const int MaximumMembers = 9;
        private readonly MediaBuffer?[] leases = new MediaBuffer?[MaximumMembers];
        private readonly Eac3EncodedFrame[] frames = new Eac3EncodedFrame[MaximumMembers];
        public int Count { get; private set; }
        public MediaTimestamp? Timestamp { get; private set; }
        public MediaDuration? Duration { get; private set; }
        public bool IsPreroll { get; private set; }
        public ReadOnlySpan<Eac3EncodedFrame> Frames => frames.AsSpan(0, Count);

        public void Add(MediaPacket input, Eac3FrameMetadata metadata, bool isPreroll)
        {
            Ac3FrameHeader header = metadata.Header;
            if (Count == MaximumMembers) { throw new InvalidDataException("The E-AC-3 frame set exceeds eight dependent substreams."); }
            if (Count == 0)
            {
                if (header.StreamType == 1 || header.SubstreamId != 0)
                { throw new InvalidDataException("An E-AC-3 frame set must begin with the main independent substream."); }
                Timestamp = input.PresentationTime; Duration = input.Duration; IsPreroll = isPreroll;
            }
            else
            {
                Ac3FrameHeader main = frames[0].Header;
                if (main.StreamType != 0 || header.StreamType != 1 || header.SubstreamId != Count - 1 ||
                    header.SampleRate != main.SampleRate || header.SamplesPerFrame != main.SamplesPerFrame)
                { throw new InvalidDataException("The individual E-AC-3 syncframe is not synchronized with its ordered frame set."); }
                if (Timestamp is MediaTimestamp start && input.PresentationTime is MediaTimestamp pts && start.CompareTo(pts) != 0)
                { throw new InvalidDataException("The dependent E-AC-3 timestamp differs from its independent frame."); }
            }
            MediaBuffer lease = input.RetainData();
            leases[Count] = lease; frames[Count++] = new(lease.Memory, metadata);
        }

        public void Clear()
        {
            for (int index = 0; index < Count; index++) { leases[index]?.Dispose(); leases[index] = null; }
            Array.Clear(frames, 0, Count); Count = 0; Timestamp = null; Duration = null; IsPreroll = false;
        }
    }
}
