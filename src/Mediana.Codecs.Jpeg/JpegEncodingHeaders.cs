// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Video;
using Mediana.Formats.Color;

namespace Mediana.Codecs.Jpeg
{
    internal static class JpegEncodingHeaders
    {
        internal static void Write(JpegOutput output, VideoFormat format, SampleAspectRatio aspect, ReadOnlySpan<byte> quantization,
            int horizontal, int vertical, bool monochrome)
        {
            output.Marker(216);
            output.Marker(224); output.Word(16);
            output.Bytes("JFIF\0"u8); output.Byte(1); output.Byte(2); output.Byte(0);
            output.Word(aspect.Denominator); output.Word(aspect.Numerator); output.Word(0);
            WriteColor(output, aspect, monochrome);
            int components = monochrome ? 1 : 3;
            int tables = monochrome ? 1 : 2;
            output.Marker(219); output.Word(2 + tables * 65);
            for (int table = 0; table < tables; table++)
            {
                output.Byte(table);
                for (int i = 0; i < 64; i++) { output.Byte(quantization[table * 64 + JpegTransform.Zigzag[i]]); }
            }
            output.Marker(192); output.Word(8 + components * 3); output.Byte(8);
            output.Word(format.Height); output.Word(format.Width); output.Byte(components);
            output.Byte(1); output.Byte((horizontal << 4) | vertical); output.Byte(0);
            for (int component = 2; component <= components; component++)
            {
                output.Byte(component); output.Byte(17); output.Byte(1);
            }
            WriteHuffman(output, 0, JpegEncodingTables.LumaDcCounts, JpegEncodingTables.DcSymbols);
            WriteHuffman(output, 16, JpegEncodingTables.LumaAcCounts, JpegEncodingTables.LumaAcSymbols);
            if (!monochrome)
            {
                WriteHuffman(output, 1, JpegEncodingTables.ChromaDcCounts, JpegEncodingTables.DcSymbols);
                WriteHuffman(output, 17, JpegEncodingTables.ChromaAcCounts, JpegEncodingTables.ChromaAcSymbols);
            }
            output.Marker(218); output.Word(6 + components * 2); output.Byte(components);
            output.Byte(1); output.Byte(0);
            for (int component = 2; component <= components; component++)
            {
                output.Byte(component); output.Byte(17);
            }
            output.Byte(0); output.Byte(63); output.Byte(0);
        }

        private static void WriteColor(JpegOutput output, SampleAspectRatio aspect, bool monochrome)
        {
            if (aspect == SampleAspectRatio.Square)
            {
                output.Marker(225); output.Word(52);
                output.Bytes("Exif\0\0"u8);
                output.Bytes([73, 73, 42, 0, 8, 0, 0, 0, 1, 0, 105, 135, 4, 0, 1, 0, 0, 0, 26, 0, 0, 0, 0, 0, 0, 0,
                    1, 0, 1, 160, 3, 0, 1, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0]);
                return;
            }
            ReadOnlySpan<byte> profile = monochrome ? IccProfiles.SrgbGray : IccProfiles.Srgb;
            output.Marker(226); output.Word(16 + profile.Length);
            output.Bytes("ICC_PROFILE\0"u8); output.Byte(1); output.Byte(1); output.Bytes(profile);
        }

        private static void WriteHuffman(JpegOutput output, int id, ReadOnlySpan<byte> counts, ReadOnlySpan<byte> symbols)
        {
            output.Marker(196); output.Word(3 + counts.Length + symbols.Length); output.Byte(id);
            output.Bytes(counts); output.Bytes(symbols);
        }
    }
}
