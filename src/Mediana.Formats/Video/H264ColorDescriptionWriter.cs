// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Formats.Video
{
    /// <summary>Replaces SPS video signal declarations while preserving all other RBSP syntax.</summary>
    public static class H264ColorDescriptionWriter
    {
        /// <summary>Returns an owned SPS NAL without a length prefix. A null source range preserves the SPS range.</summary>
        public static MediaBuffer Rewrite(ReadOnlySpan<byte> nal, CicpColorDescription source,
            MediaBufferAllocator allocator, int maximumBytes)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(allocator);
            ArgumentOutOfRangeException.ThrowIfLessThan(maximumBytes, 8);
            source.Resolve(new VideoColorInfo());
            H264BitReader bits = H264Nal.Payload(nal, 7);
            if (!H264SequenceParser.IsSupportedProfile((int)bits.ReadBits(8)))
            {
                throw new NotSupportedException("The H.264 SPS profile is not supported for color description writing.");
            }
            H264SequenceParameterSet sequence = H264SequenceParser.Parse(nal, null, out H264ColorSyntax syntax);
            bits = H264Nal.Payload(nal, 7);
            using H264BitWriter output = new(allocator, maximumBytes, nal[0]);
            if (sequence.Vui is not null)
            {
                Copy(ref bits, output, syntax.SignalStart);
                Skip(ref bits, syntax.SignalEnd - syntax.SignalStart);
                WriteSignal(output, source, sequence.Vui);
                Copy(ref bits, output, syntax.PayloadEnd - syntax.SignalEnd);
            }
            else
            {
                Copy(ref bits, output, syntax.VuiFlag);
                bits.ReadFlag();
                output.WriteFlag(true);
                output.WriteFlag(false);
                output.WriteFlag(false);
                WriteSignal(output, source, null);
                output.WriteBits(0, 6);
            }
            return output.Finish();
        }

        private static void WriteSignal(H264BitWriter output, CicpColorDescription source, H264VuiParameters? original)
        {
            output.WriteFlag(true);
            output.WriteBits((uint)(original?.VideoFormat ?? 5), 3);
            output.WriteFlag(source.FullRange ?? original?.FullRange ?? false);
            output.WriteFlag(true);
            output.WriteBits((uint)source.Primaries, 8);
            output.WriteBits((uint)source.Transfer, 8);
            output.WriteBits((uint)source.Matrix, 8);
        }

        private static void Copy(ref H264BitReader input, H264BitWriter output, int count)
        {
            while (count > 0)
            {
                int take = Math.Min(32, count);
                output.WriteBits(input.ReadBits(take), take);
                count -= take;
            }
        }

        private static void Skip(ref H264BitReader input, int count)
        {
            while (count > 0)
            {
                int take = Math.Min(32, count);
                input.ReadBits(take);
                count -= take;
            }
        }
    }
}
