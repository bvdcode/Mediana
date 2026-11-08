// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Formats.Audio;

namespace Mediana.Codecs.Ac3
{
    internal partial class Ac3Decoder
    {
        private void ProcessEnhanced(MediaPacket input, int frameCount, int codedSamples, int presentationSamples, CancellationToken cancellationToken)
        {
            transientProcessing ??= new(allocator, format!, originalBeginning);
            transientProcessing.BeginAccessUnit(codedSamples, prerollPackets == 0 ? presentationSamples : 0, input.PresentationTime);
            for (int index = 0, offset = 0; index < frameCount; index++)
            {
                Ac3FrameHeader frame = frames[index]!;
                workspace!.Decode(input.Data.Span.Slice(offset, frame.FrameBytes), frame,
                    transientProcessing.GetFrameStorage(frame.SamplesPerFrame), cancellationToken);
                transientProcessing.AppendFrame(frame.SamplesPerFrame, workspace.TransientLocations, workspace.TransientLengths);
                offset += frame.FrameBytes;
            }
            cancellationToken.ThrowIfCancellationRequested();
        }
    }
}
