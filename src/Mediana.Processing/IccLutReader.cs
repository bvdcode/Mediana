// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Numerics;

namespace Mediana.Processing
{
    internal static class IccLutReader
    {
        internal static IccCmykLut Read(IccProfile profile, CancellationToken token)
        {
            if (profile.ColorSpace != IccProfile.U32("CMYK"u8))
            { throw new NotSupportedException("CMYK samples require a CMYK ICC profile."); }
            bool lab;
            if (profile.ConnectionSpace == IccProfile.U32("Lab "u8)) { lab = true; }
            else if (profile.ConnectionSpace == IccProfile.U32("XYZ "u8)) { lab = false; }
            else { throw new NotSupportedException("ICC CMYK conversion requires Lab or XYZ connection-space values."); }
            if (!profile.TryTag("A2B1"u8, out ReadOnlyMemory<byte> tag) && !profile.TryTag("A2B0"u8, out tag))
            { throw new NotSupportedException("ICC CMYK conversion requires a supported AToB transform."); }
            ReadOnlySpan<byte> bytes = tag.Span;
            if (bytes.Length < 12 || IccProfile.U32(bytes[4..]) != 0 || bytes[8] != 4 || bytes[9] != 3)
            { throw new InvalidDataException("ICC CMYK transform requires four input and three output channels."); }
            if (bytes.StartsWith("mft1"u8)) { return Legacy(tag, 1, lab, token); }
            if (bytes.StartsWith("mft2"u8)) { return Legacy(tag, 2, lab, token); }
            if (bytes.StartsWith("mAB "u8)) { return IccMultiStageLutReader.Read(tag, lab, token); }
            throw new NotSupportedException("ICC CMYK conversion supports lut8, lut16 and lutAToB transforms.");
        }

        private static IccCmykLut Legacy(ReadOnlyMemory<byte> tag, int precision, bool lab, CancellationToken token)
        {
            ReadOnlySpan<byte> bytes = tag.Span;
            int header = precision == 1 ? 48 : 52;
            if (bytes.Length < header || bytes[11] != 0) { throw new InvalidDataException("Invalid ICC legacy LUT header."); }
            for (int i = 0; i < 9; i++)
            {
                if (IccToneCurve.Fixed(bytes[(12 + i * 4)..]) != (i % 4 == 0 ? 1 : 0))
                { throw new InvalidDataException("ICC CMYK legacy LUT matrix must be identity."); }
            }
            if (!lab && precision == 1) { throw new NotSupportedException("Eight-bit ICC XYZ connection-space encoding is not defined."); }
            int inputCount = precision == 1 ? 256 : BinaryPrimitives.ReadUInt16BigEndian(bytes[48..]);
            int outputCount = precision == 1 ? 256 : BinaryPrimitives.ReadUInt16BigEndian(bytes[50..]);
            if (inputCount is < 2 or > 4096 || outputCount is < 2 or > 4096)
            { throw new InvalidDataException("ICC legacy LUT curve size is invalid."); }
            byte grid = bytes[10];
            long cells = (long)grid * grid * grid * grid;
            long total = header + (4L * inputCount + 3 * cells + 3L * outputCount) * precision;
            if (grid < 2 || total > bytes.Length) { throw new InvalidDataException("ICC legacy LUT exceeds its tag."); }
            IccToneCurve[] inputs = new IccToneCurve[4], outputs = new IccToneCurve[3];
            int position = header;
            for (int i = 0; i < 4; i++)
            {
                inputs[i] = IccToneCurve.FromTable(tag.Slice(position, inputCount * precision), precision);
                position += inputCount * precision;
            }
            int clutBytes = (int)(cells * 3 * precision);
            IccClut4 clut = new(tag.Slice(position, clutBytes), [grid, grid, grid, grid], precision, inputs, token);
            position += clutBytes;
            for (int i = 0; i < 3; i++)
            {
                outputs[i] = IccToneCurve.FromTable(tag.Slice(position, outputCount * precision), precision);
                position += outputCount * precision;
            }
            IccPcsEncoding encoding;
            if (!lab) { encoding = IccPcsEncoding.Xyz; }
            else if (precision == 2) { encoding = IccPcsEncoding.LegacyLab; }
            else { encoding = IccPcsEncoding.Lab; }
            return new(clut, outputs, null, RgbMatrix.Identity, Vector3.Zero, encoding);
        }
    }
}
