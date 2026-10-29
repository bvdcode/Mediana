// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal static class H264Support
    {
        public static string? GetUnsupportedHeaderReason(H264SequenceParameterSet sequence)
        {
            if (!sequence.FrameMbsOnly || sequence.ChromaFormat != 1 || sequence.SeparateColourPlane ||
                sequence.LumaBitDepth != 8 || sequence.ChromaBitDepth != 8)
            {
                return "Slice header parsing currently requires progressive 8-bit 4:2:0 pictures.";
            }
            return null;
        }

        public static string? GetUnsupportedReason(H264SequenceParameterSet sequence, H264PictureParameterSet picture)
        {
            string? reason = GetUnsupportedHeaderReason(sequence);
            if (reason is not null)
            {
                return reason;
            }
            if (sequence.ScalingMatrix || sequence.TransformBypass)
            {
                return "Reconstruction currently requires progressive 8-bit 4:2:0 slices with default scaling.";
            }
            return null;
        }

        public static string? GetUnsupportedReason(H264SliceHeader header)
        {
            string? reason = GetUnsupportedReason(header.Sequence, header.Picture);
            if (reason is not null)
            {
                return reason;
            }
            if (header.FirstMacroblock != 0)
            {
                return "H.264 reconstruction currently requires one complete slice per picture.";
            }
            return header.Kind switch
            {
                H264SliceKind.I or H264SliceKind.P or H264SliceKind.B => null,
                H264SliceKind.Sp or H264SliceKind.Si => "SP and SI pictures are not supported yet.",
                _ => throw new ArgumentOutOfRangeException(nameof(header))
            };
        }

        public static string? GetUnsupportedReason(H264Configuration configuration)
        {
            foreach (H264PictureParameterSet picture in configuration.Pictures.Values)
            {
                H264SequenceParameterSet sequence = configuration.Sequences[picture.SequenceId];
                string? reason = GetUnsupportedReason(sequence, picture);
                if (reason is not null)
                {
                    return reason;
                }
                if (sequence.Profile is not (66 or 77 or 88 or 100 or 110 or 122 or 244 or 44))
                {
                    return "The H.264 profile is not supported.";
                }
                if (H264PictureBufferLimits.LevelMacroblocks(sequence) is null)
                {
                    return "The H.264 level has no supported decoded picture buffer limits.";
                }
                if ((sequence.Vui?.ChromaLocationTop ?? 0) != (sequence.Vui?.ChromaLocationBottom ?? 0))
                {
                    return "Different top and bottom field chroma locations are not supported.";
                }
                H264PictureBufferLimits.Read(sequence);
                H264VideoFormat.Create(sequence);
            }
            return null;
        }
    }
}
