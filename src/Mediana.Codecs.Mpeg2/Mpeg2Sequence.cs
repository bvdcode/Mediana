// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Timing;
using Mediana.Core.Video;

namespace Mediana.Codecs.Mpeg2
{
    internal class Mpeg2Sequence
    {
        private int aspectCode;
        private int rateNumerator;
        private int rateDenominator;
        private VideoColorInfo color = new(Range: ColorRange.Limited, ChromaLocation: ChromaLocation.Left);
        public int Width { get; private set; }
        public int Height { get; private set; }
        public int Columns => (Width + 15) / 16;
        public int Rows => (Height + 15) / 16;
        public int LumaStride => Columns * 16;
        public int ChromaStride => Columns * 8;
        public int LumaBytes => checked(LumaStride * Rows * 16);
        public int SurfaceBytes => checked(LumaBytes + LumaBytes / 2);
        public bool Extended { get; private set; }
        public Mpeg2Quantization Quantization { get; } = new();
        public MediaDuration FrameDuration => new(1, new(rateDenominator, rateNumerator));

        public static Mpeg2Sequence Read(ReadOnlySpan<byte> payload)
        {
            Mpeg2BitReader bits = new(payload);
            Mpeg2Sequence sequence = new()
            {
                Width = bits.Read(12),
                Height = bits.Read(12),
                aspectCode = bits.Read(4)
            };
            (sequence.rateNumerator, sequence.rateDenominator) = bits.Read(4) switch
            {
                1 => (24000, 1001),
                2 => (24, 1),
                3 => (25, 1),
                4 => (30000, 1001),
                5 => (30, 1),
                6 => (50, 1),
                7 => (60000, 1001),
                8 => (60, 1),
                _ => throw new InvalidDataException("Invalid MPEG-2 sequence frame rate.")
            };
            bits.Skip(18);
            bits.ReadMarker();
            bits.Skip(11);
            sequence.Quantization.ReadSequenceMatrices(ref bits);
            return sequence;
        }

        public void ReadExtension(scoped ref Mpeg2BitReader bits)
        {
            if (Extended) { throw new InvalidDataException("Duplicate MPEG-2 sequence extension."); }
            int profile = bits.Read(8);
            bits.Skip(1);
            int chroma = bits.Read(2);
            Width |= bits.Read(2) << 12;
            Height |= bits.Read(2) << 12;
            bits.Skip(12);
            bits.ReadMarker();
            bits.Skip(9);
            rateNumerator *= bits.Read(2) + 1;
            rateDenominator *= bits.Read(5) + 1;
            if (Width == 0 || Height == 0) { throw new InvalidDataException("Invalid MPEG-2 sequence dimensions."); }
            if (chroma != 1 || (profile & 0xf0) is not (0x40 or 0x50))
            {
                throw new NotSupportedException("MPEG-2 decoding supports Simple and Main profiles in 4:2:0.");
            }
            Extended = true;
        }

        public void ReadDisplayExtension(scoped ref Mpeg2BitReader bits)
        {
            bits.Skip(3);
            if (bits.Flag())
            {
                color = color with
                {
                    Primaries = CicpColorimetry.GetPrimaries(bits.Read(8)),
                    Transfer = CicpColorimetry.GetTransfer(bits.Read(8)),
                    Matrix = CicpColorimetry.GetMatrix(bits.Read(8))
                };
            }
            bits.Skip(14);
            bits.ReadMarker();
            bits.Skip(14);
        }

        public VideoFormat Format(EncodedStreamInfo stream, VideoScanMode scanMode)
        {
            SampleAspectRatio? ratio = aspectCode switch
            {
                1 => SampleAspectRatio.Square,
                2 => new(checked(4 * Height), checked(3 * Width)),
                3 => new(checked(16 * Height), checked(9 * Width)),
                4 => new(checked(221 * Height), checked(100 * Width)),
                _ => null
            };
            return new(Width, Height, VideoPixelFormat.Yuv420P8, color,
                stream.AspectRatioDescription?.Resolve(ratio) ?? ratio, stream.ColorDescription, scanMode);
        }

        public PlaneLayout[] Planes() =>
        [
            new(0, LumaStride, Width, Height),
            new(LumaBytes, ChromaStride, (Width + 1) / 2, (Height + 1) / 2),
            new(LumaBytes + LumaBytes / 4, ChromaStride, (Width + 1) / 2, (Height + 1) / 2)
        ];
    }
}
