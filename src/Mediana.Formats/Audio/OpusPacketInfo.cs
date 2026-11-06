// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Formats.Audio
{
    /// <summary>Single-stream Opus framing, without entropy decoding. Durations use the 48 kHz media clock.</summary>
    public readonly record struct OpusPacketInfo(int FrameCount, int SamplesPerFrame)
    {
        public int SampleCount => FrameCount * SamplesPerFrame;

        /// <summary>Reads timing from the TOC and frame-count prefix. Does not validate payload lengths
        /// or multistream self-delimiting framing.</summary>
        public static OpusPacketInfo ReadTiming(ReadOnlySpan<byte> prefix)
        {
            if (prefix.IsEmpty) { throw new InvalidDataException("An Opus packet requires a TOC byte."); }
            int configuration = prefix[0] >> 3;
            int samples = configuration switch
            {
                >= 16 => 120 << (configuration & 3),
                >= 12 => 480 << (configuration & 1),
                _ => (configuration & 3) switch
                {
                    0 => 480,
                    1 => 960,
                    2 => 1920,
                    3 => 2880,
                    _ => throw new InvalidOperationException()
                }
            };
            int frames = (prefix[0] & 3) switch
            {
                0 => 1,
                1 or 2 => 2,
                3 => prefix.Length >= 2 ? prefix[1] & 63 : throw new InvalidDataException("The Opus frame-count byte is missing."),
                _ => throw new InvalidOperationException()
            };
            if (frames == 0 || frames * samples > 5760)
            {
                throw new InvalidDataException("An Opus packet must contain between one frame and 120 milliseconds of audio.");
            }
            return new(frames, samples);
        }

        /// <summary>Validates the complete non-self-delimited packet, including VBR lengths and padding.
        /// Does not validate compressed frame syntax.</summary>
        public static OpusPacketInfo Read(ReadOnlySpan<byte> packet)
        {
            OpusPacketInfo result = ReadTiming(packet);
            int position = 1;
            int end = packet.Length;
            switch (packet[0] & 3)
            {
                case 0:
                    CheckFrameLength(end - position);
                    break;
                case 1:
                    ValidateCbr(end - position, 2);
                    break;
                case 2:
                    int first = ReadLength(packet, ref position, end);
                    if (first > end - position) { throw new InvalidDataException("The first Opus VBR frame exceeds its packet."); }
                    CheckFrameLength(end - position - first);
                    break;
                case 3:
                    byte control = packet[position++];
                    if ((control & 64) != 0)
                    {
                        int padding;
                        do
                        {
                            if (position == end) { throw new InvalidDataException("The Opus padding length is truncated."); }
                            padding = packet[position++];
                            int length = padding == 255 ? 254 : padding;
                            if (length > end - position) { throw new InvalidDataException("Opus padding exceeds its packet."); }
                            end -= length;
                        } while (padding == 255);
                    }
                    if ((control & 128) == 0)
                    {
                        ValidateCbr(end - position, result.FrameCount);
                    }
                    else
                    {
                        int total = 0;
                        for (int frame = 1; frame < result.FrameCount; frame++)
                        {
                            total += ReadLength(packet, ref position, end);
                            if (total > end - position) { throw new InvalidDataException("Opus VBR lengths exceed their packet."); }
                        }
                        CheckFrameLength(end - position - total);
                    }
                    break;
                default:
                    throw new InvalidOperationException();
            }
            return result;
        }

        private static int ReadLength(ReadOnlySpan<byte> packet, ref int position, int end)
        {
            if (position == end) { throw new InvalidDataException("An Opus frame length is missing."); }
            int value = packet[position++];
            if (value >= 252)
            {
                if (position == end) { throw new InvalidDataException("A two-byte Opus frame length is truncated."); }
                value += packet[position++] * 4;
            }
            return value;
        }

        private static void ValidateCbr(int length, int frames)
        {
            if (length % frames != 0) { throw new InvalidDataException("Opus CBR frames have unequal lengths."); }
            CheckFrameLength(length / frames);
        }

        private static void CheckFrameLength(int length)
        {
            if (length is < 0 or > 1275) { throw new InvalidDataException("An Opus frame exceeds its permitted length."); }
        }
    }
}
