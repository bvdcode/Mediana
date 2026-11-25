// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Formats.Video
{
    internal static class H264SequenceDescriptionWriter
    {
        public static MediaBuffer Rewrite(ReadOnlySpan<byte> nal, VideoColorDescription? sourceColor,
            VideoAspectRatioDescription? aspectRatio, MediaBufferAllocator allocator, int maximumBytes)
        {
            ArgumentNullException.ThrowIfNull(allocator);
            ArgumentOutOfRangeException.ThrowIfLessThan(maximumBytes, 8);
            H264BitReader bits = H264Nal.Payload(nal, 7);
            if (!H264SequenceParser.IsSupportedProfile((int)bits.ReadBits(8)))
            {
                throw new NotSupportedException("The H.264 SPS profile is not supported for source description writing.");
            }
            H264SequenceParameterSet sequence = H264SequenceParser.Parse(nal, null, out H264VuiSyntax syntax);
            CicpColorDescription? color = null;
            (int Top, int Bottom)? chroma = null;
            if (sourceColor is not null)
            {
                H264VuiParameters original = sequence.Vui ?? new H264VuiParameters();
                color = sourceColor.ResolveCicp(new(original.ColorPrimaries, original.TransferCharacteristics,
                    original.MatrixCoefficients, original.FullRange)) ??
                    throw new NotSupportedException("The source color description cannot be represented in H.264 SPS parameters.");
                color.Resolve(new VideoColorInfo());
                chroma = ResolveChroma(sourceColor, sequence, original);
            }
            SampleAspectRatio? originalAspect = sequence.Vui is { AspectRatioWidth: > 0, AspectRatioHeight: > 0 } vui
                ? new SampleAspectRatio(vui.AspectRatioWidth, vui.AspectRatioHeight) : null;
            SampleAspectRatio? aspect = aspectRatio?.Resolve(originalAspect) ?? originalAspect;
            if (aspect is { Numerator: > ushort.MaxValue } or { Denominator: > ushort.MaxValue })
            {
                throw new NotSupportedException("The source pixel aspect ratio exceeds H.264 extended SAR spacing limits.");
            }
            if (color is null && chroma is null && aspect == originalAspect)
            {
                if (nal.Length > maximumBytes)
                {
                    throw new NotSupportedException("The H.264 SPS exceeds the configured parameter set byte limit.");
                }
                using WritableMediaBuffer unchanged = allocator.Allocate(nal.Length);
                nal.CopyTo(unchanged.Memory.Span);
                return unchanged.Publish();
            }
            bits = H264Nal.Payload(nal, 7);
            using H264BitWriter output = new(allocator, maximumBytes, nal[0]);
            if (sequence.Vui is not null)
            {
                Copy(ref bits, output, syntax.VuiFlag + 1);
                if (aspect != originalAspect)
                {
                    Skip(ref bits, syntax.AspectEnd - bits.BitPosition);
                    WriteAspect(output, aspect);
                }
                Copy(ref bits, output, syntax.SignalStart - bits.BitPosition);
                if (color is not null)
                {
                    Skip(ref bits, syntax.SignalEnd - bits.BitPosition);
                    WriteSignal(output, color, sequence.Vui);
                }
                if (chroma is { } location)
                {
                    Skip(ref bits, syntax.ChromaEnd - bits.BitPosition);
                    WriteChroma(output, location);
                }
                Copy(ref bits, output, syntax.PayloadEnd - bits.BitPosition);
            }
            else
            {
                Copy(ref bits, output, syntax.VuiFlag);
                output.WriteFlag(true);
                WriteAspect(output, aspect);
                output.WriteFlag(false);
                if (color is not null)
                {
                    WriteSignal(output, color, null);
                }
                else
                {
                    output.WriteFlag(false);
                }
                WriteChroma(output, chroma);
                output.WriteBits(0, 5);
            }
            return output.Finish();
        }

        private static void WriteAspect(H264BitWriter output, SampleAspectRatio? aspect)
        {
            output.WriteFlag(aspect is not null);
            if (aspect is SampleAspectRatio ratio)
            {
                if (ratio == SampleAspectRatio.Square)
                {
                    output.WriteBits(1, 8);
                }
                else
                {
                    output.WriteBits(255, 8);
                    output.WriteBits((uint)ratio.Numerator, 16);
                    output.WriteBits((uint)ratio.Denominator, 16);
                }
            }
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

        private static (int Top, int Bottom)? ResolveChroma(VideoColorDescription source, H264SequenceParameterSet sequence,
            H264VuiParameters original)
        {
            if (sequence.ChromaFormat != 1)
            {
                ChromaLocation codecLocation = sequence.ChromaFormat == 0 ? ChromaLocation.Unspecified : ChromaLocation.TopLeft;
                if (source.Resolve(new(ChromaLocation: codecLocation)).ChromaLocation != codecLocation)
                {
                    throw new NotSupportedException("The source chroma location cannot be represented in this H.264 chroma format.");
                }
                return null;
            }
            int originalTop = original.ChromaLocationTop ?? 0;
            int originalBottom = original.ChromaLocationBottom ?? 0;
            int top = ResolveLocation(source, originalTop);
            int bottom = originalTop == originalBottom ? top : ResolveLocation(source, originalBottom);
            return top == originalTop && bottom == originalBottom ? null : (top, bottom);
        }

        private static int ResolveLocation(VideoColorDescription source, int code) => H264VuiParameters.GetChromaLocationCode(
            source.Resolve(new(ChromaLocation: H264VuiParameters.GetChromaLocation(code))).ChromaLocation);

        private static void WriteChroma(H264BitWriter output, (int Top, int Bottom)? locations)
        {
            output.WriteFlag(locations is not null);
            if (locations is { } chroma)
            {
                output.WriteUnsigned((uint)chroma.Top);
                output.WriteUnsigned((uint)chroma.Bottom);
            }
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
