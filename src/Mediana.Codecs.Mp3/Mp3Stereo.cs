// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Audio;

namespace Mediana.Codecs.Mp3
{
    internal static class Mp3Stereo
    {
        private const double InverseRootTwo = 0.7071067811865476;

        public static void Apply(Mp3FrameHeader header, Mp3GranuleInfo leftInfo, Mp3GranuleInfo rightInfo,
            ReadOnlySpan<Mp3SpectralBand> bands, ReadOnlySpan<byte> positions, ReadOnlySpan<byte> limits,
            Span<double> left, Span<double> right)
        {
            if (header.ChannelMode != Mp3ChannelMode.JointStereo || header.ModeExtension == 0)
            {
                return;
            }
            if (leftInfo.BlockType != rightInfo.BlockType || leftInfo.MixedBlock != rightInfo.MixedBlock)
            {
                throw new InvalidDataException("MP3 joint stereo channels must use matching window layouts.");
            }
            bool intensity = (header.ModeExtension & 1) != 0;
            bool midSide = (header.ModeExtension & 2) != 0;
            Span<bool> nonzero = stackalloc bool[4];
            nonzero.Clear();
            for (int index = bands.Length - 1; index >= 0; index--)
            {
                Mp3SpectralBand band = bands[index];
                int slot = band.Window + 1;
                for (int offset = band.Offset; offset < band.Offset + band.Width; offset++)
                {
                    nonzero[slot] |= right[offset] != 0;
                }
                bool eligible = intensity && !nonzero[slot];
                if (band.Window < 0)
                {
                    eligible &= !nonzero[1] && !nonzero[2] && !nonzero[3];
                }
                int positionIndex = index;
                if (band.Window < 0 && band.Band == 21)
                {
                    positionIndex--;
                }
                else if (band.Window >= 0 && band.Band == 12)
                {
                    positionIndex -= 3;
                }
                int position = positions[positionIndex];
                bool mpeg1 = header.Version == MpegAudioVersion.Mpeg1;
                if (eligible && mpeg1 && position > 7)
                {
                    throw new InvalidDataException("The MP3 intensity stereo position is invalid.");
                }
                eligible &= mpeg1 ? position != 7 : position != limits[positionIndex];
                if (eligible)
                {
                    (double leftGain, double rightGain) = Gains(position, mpeg1, rightInfo.ScaleFactorCompression & 1);
                    for (int offset = band.Offset; offset < band.Offset + band.Width; offset++)
                    {
                        double value = left[offset];
                        left[offset] = value * leftGain;
                        right[offset] = value * rightGain;
                    }
                }
                else if (midSide)
                {
                    for (int offset = band.Offset; offset < band.Offset + band.Width; offset++)
                    {
                        double middle = left[offset];
                        double side = right[offset];
                        left[offset] = (middle + side) * InverseRootTwo;
                        right[offset] = (middle - side) * InverseRootTwo;
                    }
                }
            }
        }

        private static (double Left, double Right) Gains(int position, bool mpeg1, int scale)
        {
            if (mpeg1)
            {
                if (position == 6)
                {
                    return (1, 0);
                }
                double ratio = Math.Tan(position * Math.PI / 12);
                return (ratio / (1 + ratio), 1 / (1 + ratio));
            }
            int steps = (position + 1) / 2;
            double gain = Math.Pow(2, -steps * (scale == 0 ? 0.25 : 0.5));
            return position % 2 == 0 ? (1, gain) : (gain, 1);
        }
    }
}
