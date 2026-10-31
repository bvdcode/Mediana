// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core;

namespace Mediana.Codecs.Aac
{
    public partial class AacEncoder
    {
        private CodecOutput<MediaPacket> ReceiveCore(CancellationToken token)
        {
            if (drained)
            {
                return CodecOutput<MediaPacket>.EndOfStream;
            }
            BufferInput(token);
            if (buffered < 2 * FrameSamples && (!completed || input is not null))
            {
                return CodecOutput<MediaPacket>.InputRequired;
            }
            if (firstTimestamp is null)
            {
                drained = true;
                return CodecOutput<MediaPacket>.EndOfStream;
            }
            bool final = completed && input is null && buffered == 0;
            if (!currentAnalyzed)
            {
                currentTransient = Analyze(FrameSamples, token);
                currentAnalyzed = true;
            }
            bool nextTransient = Analyze(2 * FrameSamples, token);
            int sequence = windowSelector.Select(currentTransient, nextTransient);
            MediaPacket packet = Encode(sequence, token);
            currentTransient = nextTransient;
            packets++;
            buffered = Math.Max(0, buffered - FrameSamples);
            for (int channel = 0; channel < format.Channels.Count; channel++)
            {
                Span<double> samples = workspace!.Pcm(channel);
                samples[FrameSamples..].CopyTo(samples);
                samples[(2 * FrameSamples)..].Clear();
            }
            if (final)
            {
                drained = true;
                workspace!.Dispose();
                workspace = null;
            }
            return CodecOutput<MediaPacket>.Available(packet);
        }

        private void BufferInput(CancellationToken token)
        {
            if (input is null)
            {
                return;
            }
            workspace ??= new(allocator, format.Channels.Count);
            int count = Math.Min(2 * FrameSamples - buffered, input.SamplesPerChannel - inputOffset);
            ReadOnlySpan<float> pcm = MemoryMarshal.Cast<byte, float>(input.GetPlane(0).Span);
            for (int sample = 0; sample < count; sample++)
            {
                if ((sample & 255) == 0)
                {
                    token.ThrowIfCancellationRequested();
                }
                for (int channel = 0; channel < format.Channels.Count; channel++)
                {
                    float value = pcm[(inputOffset + sample) * format.Channels.Count + channel];
                    if (!float.IsFinite(value))
                    {
                        throw new InvalidDataException("AAC encoding requires finite PCM samples.");
                    }
                    workspace.Pcm(channel)[FrameSamples + buffered + sample] = value * 32768d;
                }
            }
            buffered += count;
            inputOffset += count;
            if (inputOffset == input.SamplesPerChannel)
            {
                input.Dispose();
                input = null;
            }
        }

        private bool Analyze(int start, CancellationToken token)
        {
            bool left = leftDetector.Analyze(workspace!.Pcm(0).Slice(start, FrameSamples), token);
            bool right = format.Channels.Count == 2 && rightDetector.Analyze(workspace.Pcm(1).Slice(start, FrameSamples), token);
            return left || right;
        }
    }
}
