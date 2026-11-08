// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Timing;
using Mediana.Formats.Audio;

namespace Mediana.Codecs.Ac3
{
    internal partial class Ac3Decoder
    {
        private void ProcessEnhanced(MediaPacket input, CancellationToken cancellationToken)
        {
            enhancedAccessUnit ??= new(expectation);
            try
            {
                enhancedAccessUnit.Read(input.Data);
                ProcessFrames(enhancedAccessUnit.Frames, enhancedAccessUnit.Samples, enhancedAccessUnit.FirstGroupCount,
                    input.PresentationTime, input.Duration, prerollPackets != 0, cancellationToken);
            }
            finally { enhancedAccessUnit.Clear(); }
        }

        private void ProcessIndividual(MediaPacket input, CancellationToken token)
        {
            Ac3FrameHeader header = Ac3FrameHeader.Read(input.Data.Span);
            if (!header.IsEnhanced || header.FrameBytes != input.Data.Length)
            { throw new InvalidDataException("The E-AC-3 packet must contain exactly one complete syncframe."); }
            Ac3FrameDecoder.ValidateFrame(input.Data.Span, header);
            Eac3FrameMetadata metadata = Eac3FrameMetadata.Read(input.Data.Span, header);
            if (header.StreamType != 1) { FlushIndividual(token); }
            frameSetAssembly ??= new();
            frameSetAssembly.Add(input, metadata, prerollPackets != 0);
        }

        private void FlushIndividual(CancellationToken token)
        {
            if (frameSetAssembly is null || frameSetAssembly.Count == 0) { return; }
            try
            {
                ReadOnlySpan<Eac3EncodedFrame> group = frameSetAssembly.Frames;
                ProcessFrames(group, group[0].Header.SamplesPerFrame, group.Length, frameSetAssembly.Timestamp,
                    frameSetAssembly.Duration, frameSetAssembly.IsPreroll, token);
            }
            finally { frameSetAssembly.Clear(); }
        }

        private void ProcessFrames(ReadOnlySpan<Eac3EncodedFrame> encoded, int codedSamples, int firstGroupCount,
            MediaTimestamp? timestamp, MediaDuration? duration, bool isPreroll, CancellationToken cancellationToken)
        {
            enhancedProgram ??= new(encoded[..firstGroupCount], allocator, applyDynamicRangeCompression, expectation);
            format = enhancedProgram.Format;
            int samples = AudioPacketTiming.GetPresentationSamples(duration, format.SampleRate, codedSamples);
            transientProcessing ??= new(allocator, format, originalBeginning);
            transientProcessing.BeginAccessUnit(codedSamples, isPreroll ? 0 : samples, timestamp);
            for (int start = 0; start < encoded.Length;)
            {
                int end = start + 1;
                while (end < encoded.Length && encoded[end].Header.StreamType == 1) { end++; }
                ReadOnlySpan<Eac3EncodedFrame> group = encoded[start..end];
                enhancedProgram.Validate(group);
                int frameSamples = group[0].Header.SamplesPerFrame;
                enhancedProgram.Decode(group, transientProcessing.GetFrameStorage(frameSamples), cancellationToken);
                transientProcessing.AppendFrame(frameSamples, enhancedProgram.TransientLocations[..format.Channels.Count],
                    enhancedProgram.TransientLengths[..format.Channels.Count]);
                start = end;
            }
            cancellationToken.ThrowIfCancellationRequested();
        }
    }
}
