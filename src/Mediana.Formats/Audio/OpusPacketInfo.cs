// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Formats.Audio
{
    /// <summary>Single-stream Opus framing, without entropy decoding. Durations use the 48 kHz media clock.</summary>
    public readonly record struct OpusPacketInfo(int FrameCount, int SamplesPerFrame)
    {
        public const int MaximumFrameSamples = 2880;
        public const int MaximumPacketSamples = 5760;

        public int SampleCount => FrameCount * SamplesPerFrame;

        /// <summary>Reads timing from the TOC and frame-count prefix. Does not validate payload lengths
        /// or multistream self-delimiting framing.</summary>
        public static OpusPacketInfo ReadTiming(ReadOnlySpan<byte> prefix)
        {
            if (prefix.IsEmpty) { throw new InvalidDataException("An Opus packet requires a TOC byte."); }
            int samples = OpusPacketConfiguration.FromToc(prefix[0]).SamplesPerFrame;
            int frames = (prefix[0] & 3) switch
            {
                0 => 1,
                1 or 2 => 2,
                3 => prefix.Length >= 2 ? prefix[1] & 63 : throw new InvalidDataException("The Opus frame-count byte is missing."),
                _ => throw new InvalidOperationException()
            };
            if (frames == 0 || frames * samples > MaximumPacketSamples)
            {
                throw new InvalidDataException("An Opus packet must contain between one frame and 120 milliseconds of audio.");
            }
            return new(frames, samples);
        }

        /// <summary>Validates the complete non-self-delimited packet, including VBR lengths and padding.
        /// Does not validate compressed frame syntax.</summary>
        public static OpusPacketInfo Read(ReadOnlySpan<byte> packet) =>
            ReadCore(packet, default, false, false, out _);

        /// <summary>Validates a complete packet and returns borrowed frame ranges excluding all headers and padding.
        /// The destination requires at least FrameCount slots and is changed only after validation succeeds.</summary>
        public static OpusPacketInfo ReadFrames(ReadOnlySpan<byte> packet, Span<Range> frames) =>
            ReadCore(packet, frames, true, false, out _);

        /// <summary>Reads one self-delimited stream from a multistream packet, using RFC 6716 appendix B.
        /// Consumed bytes include this stream's padding and exclude all following streams.</summary>
        public static OpusPacketInfo ReadSelfDelimitedFrames(ReadOnlySpan<byte> packet, Span<Range> frames, out int consumedBytes) =>
            ReadCore(packet, frames, true, true, out consumedBytes);

        private static OpusPacketInfo ReadCore(ReadOnlySpan<byte> packet, Span<Range> frames,
            bool writeFrames, bool selfDelimited, out int consumedBytes)
        {
            OpusPacketInfo result = ReadTiming(packet);
            if (writeFrames && frames.Length < result.FrameCount)
            {
                throw new ArgumentException("The Opus frame range destination is too short.", nameof(frames));
            }
            Span<int> lengths = stackalloc int[result.FrameCount];
            int position = 1;
            int padding = 0;
            bool constant = true;
            switch (packet[0] & 3)
            {
                case 0:
                    break;
                case 1:
                    break;
                case 2:
                    constant = false;
                    lengths[0] = ReadLength(packet, ref position);
                    break;
                case 3:
                    byte control = packet[position++];
                    if ((control & 64) != 0)
                    {
                        int extension;
                        do
                        {
                            if (position == packet.Length) { throw new InvalidDataException("The Opus padding length is truncated."); }
                            extension = packet[position++];
                            padding = checked(padding + (extension == 255 ? 254 : extension));
                            if (padding > packet.Length - position) { throw new InvalidDataException("Opus padding exceeds its packet."); }
                        } while (extension == 255);
                    }
                    constant = (control & 128) == 0;
                    if (!constant)
                    {
                        for (int frame = 0; frame < result.FrameCount - 1; frame++)
                        {
                            lengths[frame] = ReadLength(packet, ref position);
                        }
                    }
                    break;
                default:
                    throw new InvalidOperationException();
            }
            int known = 0;
            if (!constant)
            {
                for (int frame = 0; frame < result.FrameCount - 1; frame++) { known += lengths[frame]; }
            }
            int last;
            if (selfDelimited) { last = ReadLength(packet, ref position); }
            else
            {
                int remaining = packet.Length - position - padding - known;
                if (remaining < 0) { throw new InvalidDataException("Opus frame lengths exceed their packet."); }
                if (constant && remaining % result.FrameCount != 0) { throw new InvalidDataException("Opus CBR frames have unequal lengths."); }
                last = constant ? remaining / result.FrameCount : remaining;
            }
            if (constant) { lengths.Fill(last); }
            else { lengths[^1] = last; }
            long total = (long)position + padding;
            foreach (int length in lengths)
            {
                CheckFrameLength(length);
                total += length;
            }
            if (total > packet.Length) { throw new InvalidDataException("Opus frame lengths exceed their packet."); }
            consumedBytes = (int)total;
            if (writeFrames)
            {
                for (int frame = 0; frame < lengths.Length; frame++)
                {
                    frames[frame] = new(position, position + lengths[frame]);
                    position += lengths[frame];
                }
            }
            return result;
        }

        private static int ReadLength(ReadOnlySpan<byte> packet, ref int position)
        {
            if (position == packet.Length) { throw new InvalidDataException("An Opus frame length is missing."); }
            int value = packet[position++];
            if (value >= 252)
            {
                if (position == packet.Length) { throw new InvalidDataException("A two-byte Opus frame length is truncated."); }
                value += packet[position++] * 4;
            }
            return value;
        }

        private static void CheckFrameLength(int length)
        {
            if (length is < 0 or > 1275) { throw new InvalidDataException("An Opus frame exceeds its permitted length."); }
        }
    }
}
