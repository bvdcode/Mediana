// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Transforms;

namespace Mediana.Codecs.Mpeg2
{
    internal ref struct Mpeg2SliceDecoder(Mpeg2Sequence sequence, Mpeg2PictureHeader picture,
        Span<byte> pixels, ReadOnlySpan<byte> forward, ReadOnlySpan<byte> backward)
    {
        private readonly Span<byte> pixels = pixels;
        private readonly ReadOnlySpan<byte> forward = forward;
        private readonly ReadOnlySpan<byte> backward = backward;
        private Mpeg2MotionVector forwardVector;
        private Mpeg2MotionVector backwardVector;
        private Mpeg2MacroblockFlags previousFlags;
        private int stateY;
        private int stateCb;
        private int stateCr;
        private int scale;

        public int Read(int vertical, ReadOnlySpan<byte> payload, int nextMacroblock, CancellationToken token)
        {
            Mpeg2BitReader bits = new(payload);
            if (sequence.Height > 2800) { vertical += bits.Read(3) << 7; }
            if (vertical > sequence.Rows) { throw new InvalidDataException("The MPEG-2 slice starts outside the picture."); }
            scale = Mpeg2Quantization.ReadScale(ref bits, picture.NonlinearQuantizer);
            if (bits.Flag())
            {
                bits.Skip(8);
                while (bits.Flag()) { bits.Skip(8); }
            }
            ResetDc();
            int total = checked(sequence.Columns * sequence.Rows);
            int address = checked((vertical - 1) * sequence.Columns - 1 + ReadIncrement(ref bits, sequence.Columns));
            if (address != nextMacroblock)
            {
                throw new InvalidDataException("MPEG-2 slices are not contiguous in macroblock order.");
            }
            int count = 0;
            Span<int> coefficients = stackalloc int[64];
            while (true)
            {
                if ((count++ & 15) == 0) { token.ThrowIfCancellationRequested(); }
                ReadMacroblock(ref bits, address, coefficients);
                address++;
                if (bits.AtSliceEnd()) { return address; }
                int advance = ReadIncrement(ref bits, total - address);
                int end = checked(address + advance - 1);
                while (address < end)
                {
                    token.ThrowIfCancellationRequested();
                    PredictSkipped(address++);
                }
            }
        }

        private void ReadMacroblock(scoped ref Mpeg2BitReader bits, int address, scoped Span<int> coefficients)
        {
            Mpeg2Vlc types = picture.Kind switch
            {
                Mpeg2PictureKind.Intra => Mpeg2Tables.IntraMacroblock,
                Mpeg2PictureKind.Predictive => Mpeg2Tables.PredictiveMacroblock,
                Mpeg2PictureKind.Bidirectional => Mpeg2Tables.BidirectionalMacroblock,
                _ => throw new InvalidOperationException("Unexpected MPEG-2 picture type.")
            };
            Mpeg2MacroblockFlags flags = (Mpeg2MacroblockFlags)types.Read(ref bits);
            if ((flags & Mpeg2MacroblockFlags.Quantizer) != 0)
            {
                scale = Mpeg2Quantization.ReadScale(ref bits, picture.NonlinearQuantizer);
            }
            bool intra = (flags & Mpeg2MacroblockFlags.Intra) != 0;
            if (intra)
            {
                if (picture.ConcealmentVectors)
                {
                    forwardVector = Mpeg2MotionVector.Read(ref bits, forwardVector, picture.ForwardHorizontal, picture.ForwardVertical);
                    bits.ReadMarker();
                }
                else
                {
                    forwardVector = backwardVector = default;
                }
            }
            else
            {
                if ((flags & Mpeg2MacroblockFlags.Forward) != 0)
                {
                    forwardVector = Mpeg2MotionVector.Read(ref bits, forwardVector, picture.ForwardHorizontal, picture.ForwardVertical);
                }
                if ((flags & Mpeg2MacroblockFlags.Backward) != 0)
                {
                    backwardVector = Mpeg2MotionVector.Read(ref bits, backwardVector, picture.BackwardHorizontal, picture.BackwardVertical);
                }
                if (picture.Kind == Mpeg2PictureKind.Predictive && (flags & Mpeg2MacroblockFlags.Forward) == 0)
                {
                    forwardVector = default;
                    flags |= Mpeg2MacroblockFlags.Forward;
                }
                Predict(address, flags);
                ResetDc();
            }
            previousFlags = flags;
            int pattern = intra ? 63 : (flags & Mpeg2MacroblockFlags.Pattern) != 0 ? Mpeg2Tables.CodedPattern.Read(ref bits) : 0;
            if (!intra && (flags & Mpeg2MacroblockFlags.Pattern) != 0 && pattern == 0)
            {
                throw new InvalidDataException("The coded block pattern is reserved for MPEG-2 4:2:0 pictures.");
            }
            int x = address % sequence.Columns;
            int y = address / sequence.Columns;
            for (int block = 0, mask = 32; mask != 0; block++, mask >>= 1)
            {
                if ((pattern & mask) == 0) { continue; }
                bool chroma = block >= 4;
                ref int dc = ref stateY;
                int stride = sequence.LumaStride;
                int offset;
                switch (block)
                {
                    case < 4:
                        offset = (y * 16 + (block & 2) * 4) * stride + x * 16 + (block & 1) * 8;
                        break;
                    case 4:
                        dc = ref stateCb;
                        stride = sequence.ChromaStride;
                        offset = sequence.LumaBytes + (y * stride + x) * 8;
                        break;
                    case 5:
                        dc = ref stateCr;
                        stride = sequence.ChromaStride;
                        offset = sequence.LumaBytes + sequence.LumaBytes / 4 + (y * stride + x) * 8;
                        break;
                    default:
                        throw new InvalidOperationException("Unexpected MPEG-2 block number.");
                }
                ulong nonzero = Mpeg2Coefficients.Read(ref bits, coefficients, ref dc, picture,
                    sequence.Quantization.GetMatrix(intra, chroma), chroma, intra, scale);
                BlockInverseDct8x8.Write(coefficients, pixels[offset..], stride, intra, nonzero);
            }
        }

        private void PredictSkipped(int address)
        {
            ResetDc();
            switch (picture.Kind)
            {
                case Mpeg2PictureKind.Predictive:
                    forwardVector = default;
                    Predict(address, Mpeg2MacroblockFlags.Forward);
                    break;
                case Mpeg2PictureKind.Bidirectional when (previousFlags & Mpeg2MacroblockFlags.Intra) == 0:
                    Predict(address, previousFlags);
                    break;
                default:
                    throw new InvalidDataException("The MPEG-2 macroblock cannot be skipped in this prediction mode.");
            }
        }

        private void Predict(int address, Mpeg2MacroblockFlags flags)
        {
            bool usingForward = (flags & Mpeg2MacroblockFlags.Forward) != 0;
            if (usingForward)
            {
                Mpeg2MotionCompensation.Apply(pixels, forward, sequence, address, forwardVector, false);
            }
            if ((flags & Mpeg2MacroblockFlags.Backward) != 0)
            {
                Mpeg2MotionCompensation.Apply(pixels, backward, sequence, address, backwardVector, usingForward);
            }
        }

        private void ResetDc() => stateY = stateCb = stateCr = 1 << (7 + picture.DcPrecision);

        private static int ReadIncrement(scoped ref Mpeg2BitReader bits, int maximum)
        {
            int result = 0;
            int symbol;
            do
            {
                symbol = Mpeg2Tables.AddressIncrement.Read(ref bits);
                result += symbol < 0 ? 33 : symbol;
                if (result > maximum)
                {
                    throw new InvalidDataException("The MPEG-2 macroblock address exceeds its picture.");
                }
            }
            while (symbol < 0);
            return result;
        }
    }
}
