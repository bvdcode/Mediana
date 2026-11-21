// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Formats.Audio;

namespace Mediana.Codecs.Alac
{
    internal static class AlacPacketDecoder
    {
        public static AudioFrame Decode(MediaPacket packet, AlacConfiguration configuration, AudioFormat format,
            MediaBufferAllocator allocator, CancellationToken token)
        {
            AlacBitReader bits = new(packet.Data.Span);
            int channels = 0;
            int samples = 0;
            using WritableMediaBuffer output = allocator.Allocate(format.GetBufferSize(configuration.FrameLength));
            using WritableMediaBuffer workspace = allocator.Allocate(checked(configuration.FrameLength * 2 * sizeof(int)));
            Span<int> pcm = MemoryMarshal.Cast<byte, int>(output.Memory.Span);
            Span<int> work = MemoryMarshal.Cast<byte, int>(workspace.Memory.Span);
            while (true)
            {
                token.ThrowIfCancellationRequested();
                int type = (int)bits.Read(3);
                if (type == 7)
                {
                    break;
                }
                if (type is 4 or 6)
                {
                    SkipAncillary(ref bits, type);
                    continue;
                }
                int count = type switch
                {
                    0 or 3 => 1,
                    1 => 2,
                    _ => throw new InvalidDataException("The ALAC element type is reserved.")
                };
                if (channels + count > configuration.ChannelCount)
                {
                    throw new InvalidDataException("The ALAC frame has excess channels.");
                }
                bits.Skip(4);
                if (bits.Read(12) != 0)
                {
                    throw new InvalidDataException("The ALAC element reserved bits are nonzero.");
                }
                bool partial = bits.Read(1) != 0;
                int lowBytes = (int)bits.Read(2);
                bool raw = bits.Read(1) != 0;
                uint length = partial ? bits.Read(32) : (uint)configuration.FrameLength;
                if (length == 0 || length > configuration.FrameLength || (samples != 0 && samples != length) || lowBytes == 3)
                {
                    throw new InvalidDataException("The ALAC element frame length or low-byte count is invalid.");
                }
                samples = (int)length;
                if (raw)
                {
                    for (int i = 0; i < samples; i++)
                    {
                        if ((i & 255) == 0)
                        {
                            token.ThrowIfCancellationRequested();
                        }
                        for (int c = 0; c < count; c++)
                        {
                            pcm[i * configuration.ChannelCount + channels + c] = bits.Signed(configuration.BitDepth) << (32 - configuration.BitDepth);
                        }
                    }
                }
                else
                {
                    DecodeElement(ref bits, work, pcm, configuration, channels, count, samples, lowBytes * 8, token);
                }
                channels += count;
            }
            if (channels != configuration.ChannelCount || samples == 0)
            {
                throw new InvalidDataException("The ALAC frame is missing channels.");
            }
            bits.Align();
            if (bits.Remaining != 0)
            {
                throw new InvalidDataException("Trailing data follows the ALAC frame.");
            }
            using MediaBuffer buffer = output.Publish();
            using MediaBuffer slice = buffer.Slice(0, format.GetBufferSize(samples));
            return new(format, samples, slice, packet.PresentationTime);
        }

        private static void DecodeElement(ref AlacBitReader bits, Span<int> workspace, Span<int> output,
            AlacConfiguration config, int channel, int count, int samples, int lowBits, CancellationToken token)
        {
            int mixShift = (int)bits.Read(8);
            int mixWeight = (int)bits.Read(8);
            int precision = config.BitDepth - lowBits + count - 1;
            if (precision is < 1 or > 32 || mixShift > 31)
            {
                throw new InvalidDataException("The ALAC channel precision or mixing shift is invalid.");
            }
            Span<short> coefficients = stackalloc short[62];
            Span<int> orders = stackalloc int[2];
            Span<int> shifts = stackalloc int[2];
            Span<int> factors = stackalloc int[2];
            Span<int> modes = stackalloc int[2];
            for (int c = 0; c < count; c++)
            {
                modes[c] = (int)bits.Read(4);
                shifts[c] = (int)bits.Read(4);
                factors[c] = (int)bits.Read(3);
                orders[c] = (int)bits.Read(5);
                for (int j = 0; j < orders[c]; j++)
                {
                    coefficients[c * 31 + j] = (short)bits.Signed(16);
                }
            }
            AlacBitReader low = bits;
            bits.Skip(checked(lowBits * samples * count));
            for (int c = 0; c < count; c++)
            {
                Span<int> plane = workspace.Slice(c * samples, samples);
                AlacResidual.Read(ref bits, plane, config, precision, factors[c], token);
                if (modes[c] != 0)
                {
                    AlacPrediction.Restore(plane, coefficients[..31], precision, 0, token);
                }
                AlacPrediction.Restore(plane, coefficients.Slice(c * 31, orders[c]), precision, shifts[c], token);
            }
            for (int i = 0; i < samples; i++)
            {
                int first = workspace[i];
                int second = count == 2 ? workspace[samples + i] : 0;
                if (count == 2 && mixWeight != 0)
                {
                    int right = unchecked(first - ((second * mixWeight) >> mixShift));
                    first = unchecked(right + second);
                    second = right;
                }
                int target = i * config.ChannelCount + channel;
                output[target] = unchecked(((first << lowBits) | (int)low.Read(lowBits)) << (32 - config.BitDepth));
                if (count == 2)
                {
                    output[target + 1] = unchecked(((second << lowBits) | (int)low.Read(lowBits)) << (32 - config.BitDepth));
                }
            }
        }

        private static void SkipAncillary(ref AlacBitReader bits, int type)
        {
            int length;
            switch (type)
            {
                case 4:
                    bits.Skip(4);
                    bool aligned = bits.Read(1) != 0;
                    length = (int)bits.Read(8);
                    if (length == 255)
                    {
                        length += (int)bits.Read(8);
                    }
                    if (aligned)
                    {
                        bits.Align();
                    }
                    break;
                case 6:
                    length = (int)bits.Read(4);
                    if (length == 15)
                    {
                        length += (int)bits.Read(8) - 1;
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(type));
            }
            bits.Skip(length * 8);
        }
    }
}
