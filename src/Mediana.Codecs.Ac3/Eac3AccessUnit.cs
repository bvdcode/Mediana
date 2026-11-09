// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Formats.Audio;

namespace Mediana.Codecs.Ac3
{
    internal class Eac3AccessUnit(Ac3DecoderExpectation? expectation)
    {
        private const int MaximumFrames = 6 * 9;
        private readonly Eac3EncodedFrame[] frames = new Eac3EncodedFrame[MaximumFrames];
        public int Count { get; private set; }
        public int Samples { get; private set; }
        public int FirstGroupCount { get; private set; }
        public ReadOnlySpan<Eac3EncodedFrame> Frames => frames.AsSpan(0, Count);

        public void Clear()
        {
            Array.Clear(frames, 0, Count); Count = 0; Samples = 0; FirstGroupCount = 0;
        }

        public void Read(ReadOnlyMemory<byte> data)
        {
            Clear();
            Ac3FrameHeader? main = null;
            int dependent = 0;
            for (int offset = 0; offset < data.Length;)
            {
                if (Count == MaximumFrames) { throw new InvalidDataException("The E-AC-3 access unit exceeds six complete frame sets."); }
                Ac3FrameHeader header = Ac3FrameHeader.Read(data.Span[offset..]);
                if (!header.IsEnhanced || header.FrameBytes > data.Length - offset)
                { throw new InvalidDataException("The E-AC-3 syncframe does not match its access unit."); }
                ReadOnlyMemory<byte> frame = data.Slice(offset, header.FrameBytes);
                Ac3FrameDecoder.ValidateFrame(frame.Span, header);
                Eac3FrameMetadata metadata = Eac3FrameMetadata.Read(frame.Span, header);
                if (header.StreamType == 1)
                {
                    if (main is null || main.StreamType != 0 || header.SubstreamId != dependent ||
                        header.SampleRate != main.SampleRate || header.SamplesPerFrame != main.SamplesPerFrame)
                    { throw new InvalidDataException("The dependent E-AC-3 frame is not synchronized with its ordered independent frame set."); }
                    dependent++;
                }
                else
                {
                    if (header.SubstreamId != 0)
                    { throw new NotSupportedException("Multiple independent E-AC-3 programmes require explicit presentation selection."); }
                    if (Count != 0 && FirstGroupCount == 0) { FirstGroupCount = Count; }
                    expectation?.Validate(header);
                    main = header; dependent = 0;
                    Samples += header.SamplesPerFrame;
                    if (Samples > 1536) { throw new InvalidDataException("The E-AC-3 access unit exceeds six audio blocks."); }
                }
                frames[Count++] = new(frame, metadata); offset += header.FrameBytes;
            }
            if (Count == 0) { throw new InvalidDataException("The E-AC-3 access unit is empty."); }
            if (FirstGroupCount == 0) { FirstGroupCount = Count; }
            if (expectation is not null && Samples != 1536)
            { throw new InvalidDataException("An MP4 E-AC-3 access unit requires exactly six audio blocks."); }
        }
    }
}
