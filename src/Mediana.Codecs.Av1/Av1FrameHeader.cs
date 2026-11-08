// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal readonly partial record struct Av1FrameHeader(bool DisableCdfUpdate, bool ScreenContent, int TileOffset, Av1SequenceHeader Geometry, int RenderWidth, int RenderHeight, Av1Quantization Quantization, bool ReducedTransformSet, bool SelectTransform)
    {
        private const int MaximumTileWidth = 4096;
        private const int MaximumTileArea = 4096 * 2304;

        internal static Av1FrameHeader Parse(ReadOnlySpan<byte> encoded, Av1SequenceHeader sequence)
        {
            Av1BitReader bits = new(encoded);
            if (!sequence.Tools.ReducedStillPicture)
            {
                if (bits.Flag())
                {
                    throw new NotSupportedException("AV1 show-existing reference pictures are not implemented.");
                }
                int frameType = bits.Read(2);
                if (frameType != 0)
                {
                    throw new NotSupportedException("AV1 inter and intra-only reference pictures are not implemented.");
                }
                if (!bits.Flag())
                {
                    throw new NotSupportedException("AV1 hidden key pictures are not implemented.");
                }
            }
            bool disableCdfUpdate = bits.Flag();
            bool screenContent = sequence.Tools.ForceScreenContent switch
            {
                0 => false,
                1 => true,
                2 => bits.Flag(),
                _ => throw new InvalidDataException("The AV1 sequence screen-content control is invalid.")
            };
            if (screenContent && sequence.Tools.ForceIntegerMotion == 2)
            {
                bits.Flag();
            }
            bool overrideSize = !sequence.Tools.ReducedStillPicture && bits.Flag();
            bits.Read(sequence.Tools.OrderHintBits);
            if (overrideSize)
            {
                int width = bits.Read(sequence.Tools.WidthBits) + 1;
                int height = bits.Read(sequence.Tools.HeightBits) + 1;
                if (width > sequence.Width || height > sequence.Height)
                {
                    throw new InvalidDataException("The AV1 frame dimensions exceed the declared sequence maximum.");
                }
                sequence = sequence with { Width = width, Height = height };
            }
            if (sequence.SuperResolution && bits.Flag())
            {
                throw new NotSupportedException("AV1 super-resolution reconstruction is not implemented.");
            }
            int renderWidth = sequence.Width;
            int renderHeight = sequence.Height;
            if (bits.Flag())
            {
                renderWidth = bits.Read(16) + 1;
                renderHeight = bits.Read(16) + 1;
            }
            if (screenContent && bits.Flag())
            {
                throw new NotSupportedException("AV1 intra block copy reconstruction is not implemented.");
            }
            if (!sequence.Tools.ReducedStillPicture && !disableCdfUpdate)
            {
                bits.Flag();
            }
            ReadSingleTile(ref bits, sequence);
            int quantizer = bits.Read(8);
            int yDc = ReadDelta(ref bits);
            int uDc = 0;
            int uAc = 0;
            int vDc = 0;
            int vAc = 0;
            if (!sequence.Monochrome)
            {
                bool different = sequence.SeparateUvDeltaQ && bits.Flag();
                uDc = ReadDelta(ref bits);
                uAc = ReadDelta(ref bits);
                vDc = different ? ReadDelta(ref bits) : uDc;
                vAc = different ? ReadDelta(ref bits) : uAc;
            }
            Av1Quantization quantization = new(quantizer, yDc, uDc, uAc, vDc, vAc);
            if (bits.Flag())
            {
                throw new NotSupportedException("AV1 quantization matrix syntax is not implemented.");
            }
            if (bits.Flag())
            {
                throw new NotSupportedException("AV1 segmentation is not implemented.");
            }
            if (quantizer > 0 && bits.Flag())
            {
                throw new NotSupportedException("AV1 block quantizer deltas are not implemented.");
            }
            bool selectTransform = false;
            if (!quantization.Lossless)
            {
                ReadInactiveFilters(ref bits, sequence);
                selectTransform = bits.Flag();
            }
            bool reducedTransformSet = bits.Flag();
            if (sequence.FilmGrain && bits.Flag())
            {
                throw new NotSupportedException("AV1 film grain synthesis is not implemented.");
            }
            bits.Align();
            if (bits.ByteOffset == encoded.Length)
            {
                throw new InvalidDataException("The AV1 frame has no tile payload.");
            }
            return new(disableCdfUpdate, screenContent, bits.ByteOffset, sequence, renderWidth, renderHeight, quantization, reducedTransformSet, selectTransform);
        }

        private static int ReadDelta(ref Av1BitReader bits) => bits.Flag() ? bits.ReadSigned(7) : 0;

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
