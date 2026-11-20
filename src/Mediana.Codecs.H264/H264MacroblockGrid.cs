// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal readonly struct H264MacroblockGrid
    {
        private const byte Unknown = byte.MaxValue;
        private readonly H264PictureLayout layout;
        private readonly int columns;
        private readonly Memory<byte> fieldPairs;

        public H264MacroblockGrid(H264PictureLayout layout, int columns, Memory<byte> fieldPairs)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(columns);
            int count = layout.MacroblockAdaptiveFrameField ? layout.MacroblockCount / 2 : 0;
            if (fieldPairs.Length != count)
            {
                throw new ArgumentException("The H.264 field-pair map must match the picture geometry.", nameof(fieldPairs));
            }
            this.layout = layout;
            this.columns = columns;
            this.fieldPairs = fieldPairs;
            fieldPairs.Span.Fill(Unknown);
        }

        public void SetFieldPair(int address, bool field)
        {
            ValidateAddress(address);
            if (!layout.MacroblockAdaptiveFrameField || (address & 1) != 0)
            {
                throw new ArgumentException("A field-pair mode requires an MBAFF pair address.", nameof(address));
            }
            byte value = field ? (byte)1 : (byte)0;
            ref byte existing = ref fieldPairs.Span[address / 2];
            if (existing != Unknown && existing != value)
            {
                throw new InvalidDataException("An H.264 macroblock pair changes its field mode.");
            }
            existing = value;
        }

        public bool IsField(int address)
        {
            ValidateAddress(address);
            if (!layout.MacroblockAdaptiveFrameField)
            {
                return layout.IsField;
            }
            return fieldPairs.Span[address / 2] switch
            {
                0 => false,
                1 => true,
                _ => throw new InvalidDataException("The H.264 macroblock pair has no decoded field mode.")
            };
        }

        public bool InferFieldPair(int address, int firstMacroblock)
        {
            ValidateAddress(address);
            if (!layout.MacroblockAdaptiveFrameField || (address & 1) != 0)
            {
                throw new ArgumentException("Field-pair inference requires an MBAFF pair address.", nameof(address));
            }
            int left = address - 2;
            if (address / 2 % columns != 0 && left >= firstMacroblock)
            {
                return IsField(left);
            }
            int top = address - columns * 2;
            return top >= firstMacroblock && IsField(top);
        }

        public (int X, int Y) Position(int address)
        {
            ValidateAddress(address);
            if (!layout.MacroblockAdaptiveFrameField)
            {
                return (address % columns * 16, address / columns * 16);
            }
            return (address / 2 % columns * 16, (address / (2 * columns) * 2 + (address & 1)) * 16);
        }

        public int Address(int x, int y)
        {
            if ((uint)x >= (uint)(columns * 16) || (uint)y >= (uint)(layout.MacroblockCount / columns * 16))
            {
                throw new ArgumentOutOfRangeException(nameof(x));
            }
            if (!layout.MacroblockAdaptiveFrameField)
            {
                return y / 16 * columns + x / 16;
            }
            return (y / 32 * columns + x / 16) * 2 + y / 16 % 2;
        }

        public (int X, int Y, int RowStep) PixelOrigin(int address, int width, int height)
        {
            ValidateAddress(address);
            if (!layout.MacroblockAdaptiveFrameField)
            {
                return (address % columns * width, address / columns * height, 1);
            }
            return PairPixelOrigin(address, width, height, IsField(address));
        }

        private (int X, int Y, int RowStep) PairPixelOrigin(int address, int width, int height, bool field)
        {
            int parity = address & 1;
            int top = address / (2 * columns) * 2 * height;
            return (address / 2 % columns * width, top + (field ? parity : parity * height), field ? 2 : 1);
        }

        public H264NeighbourLocation? Neighbour(int current, int x, int y, int width, int height, int firstMacroblock,
            bool? currentPairField = null)
        {
            ValidateAddress(current);
            if (x < -1 || x >= width * 2 || y < -1 || y >= height || (x >= width && y >= 0))
            {
                return null;
            }
            if (currentPairField.HasValue && !layout.MacroblockAdaptiveFrameField)
            {
                throw new ArgumentException("A pair mode can only be supplied for MBAFF neighbours.", nameof(currentPairField));
            }
            (int originX, int originY, int step) = currentPairField is bool mode
                ? PairPixelOrigin(current, width, height, mode)
                : PixelOrigin(current, width, height);
            int physicalX = originX + x;
            int physicalY = originY + y * step;
            if (physicalX < 0 || physicalX >= columns * width || physicalY < 0 || physicalY >= layout.MacroblockCount / columns * height)
            {
                return null;
            }
            int address;
            int localY;
            if (layout.MacroblockAdaptiveFrameField)
            {
                int pair = (physicalY / (2 * height) * columns + physicalX / width) * 2;
                if (pair < firstMacroblock || pair > current)
                {
                    return null;
                }
                int pairY = physicalY % (2 * height);
                bool field = pair == (current & ~1) && currentPairField is bool currentMode ? currentMode : IsField(pair);
                H264NeighbourLocation location = PairSample(pair, physicalX % width, pairY, height, field);
                address = location.Address;
                localY = location.Y;
            }
            else
            {
                address = physicalY / height * columns + physicalX / width;
                localY = physicalY % height;
            }
            return address < firstMacroblock || address > current ? null : new(address, physicalX % width, localY);
        }

        public H264NeighbourLocation? SampleLocation(int x, int y, int width, int height)
        {
            if (x < 0 || x >= columns * width || y < 0 || y >= layout.MacroblockCount / columns * height)
            {
                return null;
            }
            if (!layout.MacroblockAdaptiveFrameField)
            {
                return new(y / height * columns + x / width, x % width, y % height);
            }
            int pair = (y / (2 * height) * columns + x / width) * 2;
            return PairSample(pair, x % width, y % (2 * height), height, IsField(pair));
        }

        private static H264NeighbourLocation PairSample(int pair, int x, int y, int height, bool field) =>
            new(pair + (field ? y % 2 : y / height), x, field ? y / 2 : y % height);

        private void ValidateAddress(int address)
        {
            if ((uint)address >= (uint)layout.MacroblockCount)
            {
                throw new ArgumentOutOfRangeException(nameof(address));
            }
        }
    }
}
