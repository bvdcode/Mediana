// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal readonly struct H264CabacResidualReader : IH264ResidualReader
    {
        private readonly H264CabacReader reader;
        private readonly H264Picture target;
        private readonly H264CabacMacroblockState state;
        private readonly H264CabacBlockFlagWindow? window;
        private readonly int address;
        private readonly int left;
        private readonly int top;
        private readonly bool intra;
        private readonly bool field;

        public H264CabacResidualReader(H264CabacReader reader, H264Picture target, H264CabacMacroblockState state, int address)
        {
            this.reader = reader;
            this.target = target;
            this.state = state;
            this.address = address;
            left = state.Left(address);
            top = state.Top(address);
            intra = state.IsIntra(address);
            window = state.BlockFlags;
            field = window is null && target.Grid.IsField(address);
            window?.Begin(address, left, top, intra);
        }

        public void CompleteMacroblock() => window?.Complete();

        private int Coded8x8Context(int plane, int x, int y)
        {
            return Coded8x8Neighbour(plane, x, y, -1, 0) +
                2 * Coded8x8Neighbour(plane, x, y, 0, -1);
        }

        private int Coded8x8Neighbour(int plane, int x, int y, int dx, int dy)
        {
            if (target.Neighbour(plane, x, y, dx, dy) is not H264NeighbourLocation neighbour)
            {
                return intra ? 1 : 0;
            }
            if (state.Types[neighbour.Address] == 25)
            {
                return 1;
            }
            if (!target.Uses8x8Transform(neighbour.Address))
            {
                return 0;
            }
            (int originX, int originY) = target.Grid.Position(neighbour.Address);
            return target.Counts(plane)[target.BlockIndex(plane, originX + neighbour.X, originY + neighbour.Y)] == 0 ? 0 : 1;
        }

        public int Read(ref H264BitReader bits, H264ResidualCategory category, int plane, int x, int y, scoped Span<int> coefficients)
        {
            if (window is not null)
            {
                return ReadPacked(ref bits, window, category, plane, x, y, coefficients);
            }
            int horizontalScale = target.HorizontalScale(plane);
            int verticalScale = target.VerticalScale(plane);
            if (category == H264ResidualCategory.Luma8x8)
            {
                int count = H264CabacResidual.Read(ref bits, reader, category,
                    target.FullResolutionChroma ? Coded8x8Context(plane, x, y) : 0,
                    coefficients, target.Sequence.LumaBitDepth, field: field,
                    fullResolutionPlane: target.FullResolutionChroma ? plane : null);
                Span<byte> counts = target.Counts(plane);
                int first = target.BlockIndex(plane, x, y);
                int stride = target.Stride(plane) / 4;
                byte coded = count == 0 ? (byte)0 : (byte)1;
                counts.Slice(first, 2).Fill(coded);
                counts.Slice(first + stride, 2).Fill(coded);
                return count;
            }
            int context;
            bool dc = category is H264ResidualCategory.Intra16LumaDc or H264ResidualCategory.ChromaDc;
            if (dc)
            {
                context = DcContext(plane);
            }
            else if (target.Layout.MacroblockAdaptiveFrameField)
            {
                context = target.MappedCodedBlockContext(plane, x, y, intra);
            }
            else
            {
                Span<byte> counts = target.Counts(plane);
                int position = target.BlockIndex(plane, x, y);
                int unavailable = intra ? 1 : 0;
                bool hasLeft = left >= 0 || (x * horizontalScale & 15) != 0;
                bool hasTop = top >= 0 || (y * verticalScale & 15) != 0;
                context = hasLeft ? (counts[position - 1] != 0 ? 1 : 0) : unavailable;
                context += 2 * (hasTop ? (counts[position - target.Stride(plane) / 4] != 0 ? 1 : 0) : unavailable);
            }
            int total = H264CabacResidual.Read(ref bits, reader, category, context, coefficients, plane == 0 ? target.Sequence.LumaBitDepth : target.Sequence.ChromaBitDepth, field: field,
                fullResolutionPlane: target.FullResolutionChroma ? plane : null);
            if (dc && total != 0)
            {
                state.DcFlags[address] |= (byte)(1 << plane);
            }
            return total;
        }

        private int ReadPacked(ref H264BitReader bits, H264CabacBlockFlagWindow flags, H264ResidualCategory category,
            int plane, int x, int y, scoped Span<int> coefficients)
        {
            if (category == H264ResidualCategory.Luma8x8)
            {
                int count = H264CabacResidual.Read(ref bits, reader, category, 0, coefficients);
                if (count != 0)
                {
                    flags.Mark8x8(H264CodedBlockFlags.LumaIndex(x, y));
                }
                return count;
            }
            bool dc = category is H264ResidualCategory.Intra16LumaDc or H264ResidualCategory.ChromaDc;
            int block = dc ? 0 : H264CodedBlockFlags.BlockIndex(plane, x, y);
            int context = dc ? DcContext(plane) : flags.Context(block);
            int total = H264CabacResidual.ReadProgressiveBlock(ref bits, reader, category, context, coefficients);
            if (total != 0)
            {
                if (dc)
                {
                    state.DcFlags[address] |= (byte)(1 << plane);
                }
                else
                {
                    flags.Mark4x4(block);
                }
            }
            return total;
        }

        private int DcContext(int plane) => state.DcCondition(left, plane, intra) + 2 * state.DcCondition(top, plane, intra);
    }
}
