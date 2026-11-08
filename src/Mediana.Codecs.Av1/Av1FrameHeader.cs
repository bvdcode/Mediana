// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal readonly record struct Av1FrameHeader(bool DisableCdfUpdate, bool ScreenContent, int TileOffset)
    {
        private const int MaximumTileWidth = 4096;
        private const int MaximumTileArea = 4096 * 2304;

        internal static Av1FrameHeader Parse(ReadOnlySpan<byte> encoded, Av1SequenceHeader sequence)
        {
            Av1BitReader bits = new(encoded);
            bool disableCdfUpdate = bits.Flag();
            bool screenContent = bits.Flag();
            if (screenContent)
            {
                bits.Flag();
            }
            if (sequence.SuperResolution && bits.Flag())
            {
                throw new NotSupportedException("AV1 super-resolution reconstruction is not implemented.");
            }
            if (bits.Flag())
            {
                bits.Read(16);
                bits.Read(16);
            }
            if (screenContent && bits.Flag())
            {
                throw new NotSupportedException("AV1 intra block copy reconstruction is not implemented.");
            }
            ReadSingleTile(ref bits, sequence);
            int quantizer = bits.Read(8);
            int delta = bits.Flag() ? bits.ReadSigned(7) : 0;
            if (quantizer != 0 || delta != 0)
            {
                throw new NotSupportedException("AV1 quantized coefficient reconstruction is not implemented.");
            }
            if (bits.Flag())
            {
                throw new NotSupportedException("AV1 quantization matrix syntax is not implemented.");
            }
            if (bits.Flag())
            {
                throw new NotSupportedException("AV1 segmentation is not implemented.");
            }
            bits.Flag();
            if (sequence.FilmGrain && bits.Flag())
            {
                throw new NotSupportedException("AV1 film grain synthesis is not implemented.");
            }
            bits.Align();
            if (bits.ByteOffset == encoded.Length)
            {
                throw new InvalidDataException("The AV1 frame has no tile payload.");
            }
            return new(disableCdfUpdate, screenContent, bits.ByteOffset);
        }

        private static void ReadSingleTile(ref Av1BitReader bits, Av1SequenceHeader sequence)
        {
            int shift = sequence.Superblock128 ? 5 : 4;
            int columns = (sequence.MiColumns + (1 << shift) - 1) >> shift;
            int rows = (sequence.MiRows + (1 << shift) - 1) >> shift;
            int side = 1 << (shift + 2);
            int widthLimit = MaximumTileWidth / side;
            int areaLimit = MaximumTileArea / (side * side);
            if (columns > widthLimit || checked(columns * rows) > areaLimit)
            {
                throw new NotSupportedException("AV1 multi-tile reconstruction is not implemented.");
            }
            if (bits.Flag())
            {
                if (columns > 1 && bits.Flag())
                {
                    throw new NotSupportedException("AV1 multi-column tiles are not implemented.");
                }
                if (rows > 1 && bits.Flag())
                {
                    throw new NotSupportedException("AV1 multi-row tiles are not implemented.");
                }
            }
            else
            {
                int width = bits.ReadNs(Math.Min(columns, widthLimit)) + 1;
                if (width != columns)
                {
                    throw new NotSupportedException("AV1 multi-column tiles are not implemented.");
                }
                int height = bits.ReadNs(Math.Min(rows, areaLimit / width)) + 1;
                if (height != rows)
                {
                    throw new NotSupportedException("AV1 multi-row tiles are not implemented.");
                }
            }
        }
    }
}
