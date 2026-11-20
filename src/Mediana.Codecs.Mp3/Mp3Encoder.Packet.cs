// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Formats.Audio;

namespace Mediana.Codecs.Mp3
{
    public partial class Mp3Encoder
    {
        private CodecOutput<MediaPacket> ReceiveCore(CancellationToken token)
        {
            if (drained) { return CodecOutput<MediaPacket>.EndOfStream; }
            BufferInput(token);
            if (buffered < frameSamples && (!completed || input is not null)) { return CodecOutput<MediaPacket>.InputRequired; }
            if (firstTimestamp is null || (completed && input is null && packets * frameSamples >= InputSamplesPerChannel + PrimingSamples))
            {
                drained = true;
                Clear();
                return CodecOutput<MediaPacket>.EndOfStream;
            }
            workspace ??= new(allocator, format.Channels.Count, frameSamples);
            MediaPacket packet = Encode(token);
            packets++;
            buffered = 0;
            workspace.Pcm.Clear();
            return CodecOutput<MediaPacket>.Available(packet);
        }

        private void BufferInput(CancellationToken token)
        {
            if (input is null) { return; }
            workspace ??= new(allocator, format.Channels.Count, frameSamples);
            int count = Math.Min(frameSamples - buffered, input.SamplesPerChannel - inputOffset);
            ReadOnlySpan<float> source = MemoryMarshal.Cast<byte, float>(input.GetPlane(0).Span)
                .Slice(inputOffset * format.Channels.Count, count * format.Channels.Count);
            foreach (float value in source)
            {
                if (!float.IsFinite(value)) { throw new InvalidDataException("MP3 encoding requires finite PCM samples."); }
            }
            token.ThrowIfCancellationRequested();
            source.CopyTo(workspace.Pcm[(buffered * format.Channels.Count)..]);
            buffered += count;
            inputOffset += count;
            if (inputOffset == input.SamplesPerChannel)
            {
                input.Dispose();
                input = null;
            }
        }

        private MediaPacket Encode(CancellationToken token)
        {
            Mp3EncoderWorkspace current = workspace ?? throw new InvalidOperationException("MP3 analysis workspace is missing.");
            long budget = budgetRemainder + (long)(frameSamples / 8) * options.BitRate;
            int length = (int)(budget / format.SampleRate);
            budgetRemainder = budget % format.SampleRate;
            int count = frameSamples / 576 * format.Channels.Count;
            int remaining = (length - 4 - header.SideInfoBytes) * 8;
            Span<Mp3QuantizedGranule> granules = stackalloc Mp3QuantizedGranule[4];
            Span<double> spectrum = stackalloc double[576];
            for (int granule = 0; granule < frameSamples / 576; granule++)
            {
                for (int channel = 0; channel < format.Channels.Count; channel++)
                {
                    token.ThrowIfCancellationRequested();
                    int index = granule * format.Channels.Count + channel;
                    Mp3AnalysisFilter.Process(current.Pcm[(granule * 576 * format.Channels.Count)..], format.Channels.Count,
                        channel, current.History(channel), current.Previous(channel), spectrum);
                    int bits = Math.Min(4095, remaining / (count - index));
                    granules[index] = Mp3SpectrumQuantizer.Quantize(spectrum, format.SampleRate, bits, current.Quantized.Slice(index * 576, 576));
                    remaining -= granules[index].Bits;
                }
            }
            using WritableMediaBuffer storage = allocator.Allocate(length);
            storage.Memory.Span.Clear();
            header.Write(storage.Memory.Span[..4]);
            if (length > header.FrameBytes) { storage.Memory.Span[2] |= 2; }
            WriteFrame(storage.Memory.Span, granules[..count], current.Quantized);
            using MediaBuffer buffer = storage.Publish();
            MediaTimestamp time = new(checked(firstTimestamp!.Value + (packets * frameSamples - PrimingSamples) * sampleTicks), Stream.TimeBase);
            bool final = completed && input is null && (packets + 1) * frameSamples >= InputSamplesPerChannel + PrimingSamples;
            int padding = final ? checked((int)((packets + 1) * frameSamples - InputSamplesPerChannel - PrimingSamples)) : 0;
            if (packets != 0 && !final) { return new(Stream.StreamIndex, buffer, time, time, new(frameSamples * sampleTicks, Stream.TimeBase)); }
            using MediaPacketSideData trim = new AudioPacketTrim(packets == 0 ? PrimingSamples : 0, padding).CreateSideData(allocator);
            return new(Stream.StreamIndex, buffer, time, time, new(frameSamples * sampleTicks, Stream.TimeBase), sideData: [trim]);
        }

        private void WriteFrame(Span<byte> destination, ReadOnlySpan<Mp3QuantizedGranule> granules, ReadOnlySpan<int> quantized)
        {
            bool mpeg1 = header.Version == MpegAudioVersion.Mpeg1;
            Mp3BitWriter side = new(destination.Slice(4, header.SideInfoBytes));
            side.Write(0, mpeg1 ? 9 : 8);
            side.Write(0, mpeg1 ? (format.Channels.Count == 1 ? 5 : 3) : format.Channels.Count);
            if (mpeg1) { side.Write(0, 4 * format.Channels.Count); }
            foreach (Mp3QuantizedGranule info in granules)
            {
                side.Write((uint)info.Bits, 12);
                side.Write((uint)info.BigValues, 9);
                side.Write((uint)info.Gain, 8);
                side.Write(0, mpeg1 ? 4 : 9);
                side.Write(0, 1);
                side.Write((uint)info.Table0, 5);
                side.Write((uint)info.Table1, 5);
                side.Write((uint)info.Table2, 5);
                side.Write(Mp3SpectrumQuantizer.Region0, 4);
                side.Write(Mp3SpectrumQuantizer.Region1, 3);
                if (mpeg1) { side.Write(0, 1); }
                side.Write(0, 2);
            }
            if (side.Position != header.SideInfoBytes * 8) { throw new InvalidOperationException("The MP3 side information size is inconsistent."); }
            Mp3BitWriter data = new(destination[(4 + header.SideInfoBytes)..]);
            for (int index = 0; index < granules.Length; index++)
            {
                Mp3SpectrumQuantizer.Write(ref data, granules[index], quantized.Slice(index * 576, 576), format.SampleRate);
            }
        }
    }
}
