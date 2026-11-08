// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Formats.Audio;

namespace Mediana.Codecs.Ac3
{
    internal partial class Ac3Decoder
    {
        private void ProcessEnhanced(MediaPacket input, CancellationToken cancellationToken)
        {
            enhancedAccessUnit ??= new(expectation);
            enhancedAccessUnit.Read(input.Data.Span);
            ReadOnlySpan<Eac3EncodedFrame> encoded = enhancedAccessUnit.Frames;
            enhancedProgram ??= new(encoded[..enhancedAccessUnit.FirstGroupCount], allocator, applyDynamicRangeCompression, expectation);
            format = enhancedProgram.Format;
            int samples = AudioPacketTiming.GetPresentationSamples(input.Duration, format.SampleRate, enhancedAccessUnit.Samples);
            transientProcessing ??= new(allocator, format, originalBeginning);
            transientProcessing.BeginAccessUnit(enhancedAccessUnit.Samples, prerollPackets == 0 ? samples : 0, input.PresentationTime);
            for (int start = 0; start < encoded.Length;)
            {
                int end = start + 1;
                while (end < encoded.Length && encoded[end].Header.StreamType == 1) { end++; }
                ReadOnlySpan<Eac3EncodedFrame> group = encoded[start..end];
                enhancedProgram.Validate(group);
                int frameSamples = group[0].Header.SamplesPerFrame;
                enhancedProgram.Decode(input.Data.Span, group, transientProcessing.GetFrameStorage(frameSamples), cancellationToken);
                transientProcessing.AppendFrame(frameSamples, enhancedProgram.TransientLocations[..format.Channels.Count],
                    enhancedProgram.TransientLengths[..format.Channels.Count]);
                start = end;
            }
            cancellationToken.ThrowIfCancellationRequested();
        }
    }
}
