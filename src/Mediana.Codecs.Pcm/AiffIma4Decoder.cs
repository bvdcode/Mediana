// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Formats.Audio;

namespace Mediana.Codecs.Pcm
{
    internal class AiffIma4Decoder(int streamIndex, AiffIma4Configuration configuration, MediaBufferAllocator allocator,
        int maxSamplesPerFrame) : PcmPacketDecoder(streamIndex, maxSamplesPerFrame)
    {
        private readonly ImaAdpcmPredictor[] predictors = new ImaAdpcmPredictor[configuration.Format.Channels.Count];

        protected override AudioFrame Decode(MediaPacket packet, int maxSamples, CancellationToken cancellationToken)
        {
            ReadOnlySpan<byte> source = packet.Data.Span;
            int packets = source.Length / configuration.BlockAlignment;
            if (source.Length % configuration.BlockAlignment != 0)
            {
                throw new InvalidDataException("AIFF-C ima4 requires complete channel packets.");
            }
            int samples = checked(packets * AiffIma4Configuration.SamplesPerPacket);
            if (samples > maxSamples)
            {
                throw new NotSupportedException("The AIFF-C ima4 packet exceeds the configured sample limit.");
            }
            AudioFormat format = configuration.Format;
            using WritableMediaBuffer writable = allocator.Allocate(format.GetBufferSize(samples));
            Span<short> destination = MemoryMarshal.Cast<byte, short>(writable.Span);
            for (int packetIndex = 0; packetIndex < packets; packetIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (int channel = 0; channel < predictors.Length; channel++)
                {
                    int sourceOffset = packetIndex * configuration.BlockAlignment + channel * AiffIma4Configuration.ChannelPacketBytes;
                    int outputOffset = packetIndex * AiffIma4Configuration.SamplesPerPacket * predictors.Length + channel;
                    DecodeChannel(source.Slice(sourceOffset, AiffIma4Configuration.ChannelPacketBytes), destination,
                        outputOffset, predictors.Length, ref predictors[channel]);
                }
            }
            using MediaBuffer buffer = writable.Publish();
            return new(format, samples, buffer, packet.PresentationTime);
        }

        private static void DecodeChannel(ReadOnlySpan<byte> source, Span<short> destination, int offset, int stride,
            ref ImaAdpcmPredictor predictor)
        {
            const int indexMask = 0x7f;
            int header = BinaryPrimitives.ReadInt16BigEndian(source);
            int index = header & indexMask;
            if (index > ImaAdpcmPredictor.MaximumIndex)
            {
                throw new InvalidDataException("The AIFF-C ima4 step index is out of range.");
            }
            int sample = header & ~indexMask;
            if (predictor.Index != index || Math.Abs(predictor.Sample - sample) > indexMask)
            {
                predictor = new(sample, index);
            }
            foreach (byte codes in source[sizeof(short)..])
            {
                destination[offset] = predictor.Decode4(codes & 15);
                destination[offset + stride] = predictor.Decode4(codes >> 4);
                offset += 2 * stride;
            }
        }

        protected override void ResetPredictors() => Array.Clear(predictors);
    }
}
