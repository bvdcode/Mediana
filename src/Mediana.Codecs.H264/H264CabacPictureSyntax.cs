namespace Mediana.Codecs.H264
{
    internal class H264CabacPictureSyntax(H264CabacReader reader, H264Picture target, H264CabacMacroblockState state) : IH264ResidualReader
    {
        private int previousDelta;

        public void ReadPcm(ref H264BitReader bits, H264SliceHeader header, int address)
        {
            int x = address % header.Sequence.WidthInMbs * 16;
            int y = address / header.Sequence.WidthInMbs * 16;
            H264IntraMacroblock.ReadPcm(ref bits, target, x, y);
            state.Types[address] = 25;
            state.Patterns[address] = 47;
            state.DcFlags[address] = 7;
            target.Quantizers[address] = 0;
            previousDelta = 0;
            reader.Restart(ref bits);
        }

        public int ReadType(ref H264BitReader bits, int address)
        {
            int context = state.TypeCondition(state.Left(address)) + state.TypeCondition(state.Top(address));
            return H264CabacMacroblockSyntax.ReadIntraType(ref bits, reader, context);
        }

        public void ReadPrediction(ref H264BitReader bits, H264SliceHeader header, int address, int type)
        {
            state.Types[address] = (byte)type;
            if (type == 0 && header.Picture.Transform8x8 && reader.Read(ref bits, 399))
            {
                throw new NotSupportedException("CABAC intra 8x8 prediction is not implemented yet.");
            }
            int x = address % header.Sequence.WidthInMbs * 16;
            int y = address / header.Sequence.WidthInMbs * 16;
            for (int index = 0; index < 16; index++)
            {
                (int blockX, int blockY) = H264Picture.LumaBlockPosition(index);
                int mode = 2;
                if (type == 0)
                {
                    int predicted = target.PredictedMode(x + blockX, y + blockY, header.Picture.ConstrainedIntraPrediction);
                    mode = predicted;
                    if (!reader.Read(ref bits, 68))
                    {
                        int remaining = 0;
                        for (int bit = 0; bit < 3; bit++)
                        {
                            if (reader.Read(ref bits, 69))
                            {
                                remaining |= 1 << bit;
                            }
                        }
                        mode = remaining < predicted ? remaining : remaining + 1;
                    }
                }
                target.SetLumaMode(x + blockX, y + blockY, mode);
            }
            int chromaContext = 64 + state.ChromaModeCondition(state.Left(address)) + state.ChromaModeCondition(state.Top(address));
            int chroma = 0;
            if (reader.Read(ref bits, chromaContext))
            {
                chroma = 1;
                while (chroma < 3 && reader.Read(ref bits, 67))
                {
                    chroma++;
                }
            }
            state.ChromaModes[address] = (byte)chroma;
        }

        public int ReadPattern(ref H264BitReader bits, int address, int type)
        {
            int pattern = 0;
            if (type != 0)
            {
                pattern = ((type - 1) / 12 * 15) + (((type - 1) / 4 % 3) << 4);
            }
            else
            {
                for (int block = 0; block < 4; block++)
                {
                    int context = 73 + state.LumaPatternCondition(address, block, true, pattern) +
                        2 * state.LumaPatternCondition(address, block, false, pattern);
                    if (reader.Read(ref bits, context))
                    {
                        pattern |= 1 << block;
                    }
                }
                int contextChroma = 77 + state.ChromaPatternCondition(state.Left(address), 0) +
                    2 * state.ChromaPatternCondition(state.Top(address), 0);
                if (reader.Read(ref bits, contextChroma))
                {
                    int secondContext = 81 + state.ChromaPatternCondition(state.Left(address), 1) +
                        2 * state.ChromaPatternCondition(state.Top(address), 1);
                    pattern |= reader.Read(ref bits, secondContext) ? 32 : 16;
                }
            }
            state.Patterns[address] = (byte)pattern;
            return pattern;
        }

        public int ReadQuantizer(ref H264BitReader bits, int type, int pattern, int qp)
        {
            int delta = 0;
            if (type != 0 || pattern != 0)
            {
                if (reader.Read(ref bits, previousDelta == 0 ? 60 : 61))
                {
                    int mapped = 1;
                    while (reader.Read(ref bits, mapped == 1 ? 62 : 63))
                    {
                        if (++mapped > 52)
                        {
                            throw new InvalidDataException("The H.264 CABAC quantizer delta exceeds its permitted range.");
                        }
                    }
                    delta = (mapped & 1) == 0 ? -(mapped / 2) : (mapped + 1) / 2;
                    if (delta is < -26 or > 25)
                    {
                        throw new InvalidDataException("The H.264 CABAC quantizer delta exceeds its permitted range.");
                    }
                }
            }
            previousDelta = delta;
            return (qp + delta + 52) % 52;
        }

        public int Read(ref H264BitReader bits, H264ResidualCategory category, int plane, int x, int y, scoped Span<int> coefficients)
        {
            int context;
            int scale = plane == 0 ? 16 : 8;
            int address = y / scale * target.Sequence.WidthInMbs + x / scale;
            bool intra = state.IsIntra(address);
            bool dc = category is H264ResidualCategory.Intra16LumaDc or H264ResidualCategory.ChromaDc;
            if (dc)
            {
                context = state.DcCondition(state.Left(address), plane, intra) + 2 * state.DcCondition(state.Top(address), plane, intra);
            }
            else
            {
                Span<byte> counts = target.Counts(plane);
                int position = target.BlockIndex(plane, x, y);
                int unavailable = intra ? 1 : 0;
                context = x == 0 ? unavailable : counts[position - 1] != 0 ? 1 : 0;
                context += 2 * (y == 0 ? unavailable : counts[position - target.Stride(plane) / 4] != 0 ? 1 : 0);
            }
            int total = H264CabacResidual.Read(ref bits, reader, category, context, coefficients);
            if (dc && total != 0)
            {
                state.DcFlags[address] |= (byte)(1 << plane);
            }
            return total;
        }
    }
}
