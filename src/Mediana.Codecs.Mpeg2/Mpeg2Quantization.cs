// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Mpeg2
{
    internal class Mpeg2Quantization
    {
        // ITU-T H.262 Tables 7-2 through 7-6.
        public static ReadOnlySpan<byte> ZigZag =>
        [
            0, 1, 8, 16, 9, 2, 3, 10, 17, 24, 32, 25, 18, 11, 4, 5,
            12, 19, 26, 33, 40, 48, 41, 34, 27, 20, 13, 6, 7, 14, 21, 28,
            35, 42, 49, 56, 57, 50, 43, 36, 29, 22, 15, 23, 30, 37, 44, 51,
            58, 59, 52, 45, 38, 31, 39, 46, 53, 60, 61, 54, 47, 55, 62, 63
        ];

        public static ReadOnlySpan<byte> AlternateScan =>
        [
            0, 8, 16, 24, 1, 9, 2, 10, 17, 25, 32, 40, 48, 56, 57, 49,
            41, 33, 26, 18, 3, 11, 4, 12, 19, 27, 34, 42, 50, 58, 35, 43,
            51, 59, 20, 28, 5, 13, 6, 14, 21, 29, 36, 44, 52, 60, 37, 45,
            53, 61, 22, 30, 7, 15, 23, 31, 38, 46, 54, 62, 39, 47, 55, 63
        ];

        private static ReadOnlySpan<byte> Nonlinear =>
            [0, 1, 2, 3, 4, 5, 6, 7, 8, 10, 12, 14, 16, 18, 20, 22,
             24, 28, 32, 36, 40, 44, 48, 52, 56, 64, 72, 80, 88, 96, 104, 112];

        private byte[] intra =
        [
            8, 16, 19, 22, 26, 27, 29, 34,
            16, 16, 22, 24, 27, 29, 34, 37,
            19, 22, 26, 27, 29, 34, 34, 38,
            22, 22, 26, 27, 29, 34, 37, 40,
            22, 26, 27, 29, 32, 35, 40, 48,
            26, 27, 29, 32, 35, 40, 48, 58,
            26, 27, 29, 34, 38, 46, 56, 69,
            27, 29, 35, 38, 46, 56, 69, 83
        ];
        private byte[] inter = CreateInter();
        private byte[]? chromaIntra;
        private byte[]? chromaInter;

        public ReadOnlySpan<byte> GetMatrix(bool isIntra, bool chroma) =>
            isIntra ? (chroma ? chromaIntra ?? intra : intra) : (chroma ? chromaInter ?? inter : inter);

        public void ReadSequenceMatrices(scoped ref Mpeg2BitReader bits)
        {
            if (bits.Flag()) { intra = ReadMatrix(ref bits); }
            if (bits.Flag()) { inter = ReadMatrix(ref bits); }
        }

        public void ReadExtension(scoped ref Mpeg2BitReader bits)
        {
            if (bits.Flag())
            {
                intra = ReadMatrix(ref bits);
                chromaIntra = null;
            }
            if (bits.Flag())
            {
                inter = ReadMatrix(ref bits);
                chromaInter = null;
            }
            if (bits.Flag()) { chromaIntra = ReadMatrix(ref bits); }
            if (bits.Flag()) { chromaInter = ReadMatrix(ref bits); }
        }

        public static int ReadScale(scoped ref Mpeg2BitReader bits, bool nonlinear)
        {
            int code = bits.Read(5);
            if (code == 0) { throw new InvalidDataException("The MPEG-2 quantizer scale is zero."); }
            return nonlinear ? Nonlinear[code] : code * 2;
        }

        private static byte[] ReadMatrix(scoped ref Mpeg2BitReader bits)
        {
            byte[] result = new byte[64];
            foreach (int offset in ZigZag)
            {
                int value = bits.Read(8);
                if (value == 0) { throw new InvalidDataException("An MPEG-2 quantization matrix element is zero."); }
                result[offset] = (byte)value;
            }
            return result;
        }

        private static byte[] CreateInter()
        {
            byte[] matrix = new byte[64];
            matrix.AsSpan().Fill(16);
            return matrix;
        }
    }
}
