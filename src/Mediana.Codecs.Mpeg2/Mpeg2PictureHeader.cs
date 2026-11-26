// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Video;

namespace Mediana.Codecs.Mpeg2
{
    internal class Mpeg2PictureHeader(Mpeg2PictureKind kind)
    {
        public Mpeg2PictureKind Kind { get; } = kind;
        public int DcPrecision { get; private set; }
        public int ForwardHorizontal { get; private set; }
        public int ForwardVertical { get; private set; }
        public int BackwardHorizontal { get; private set; }
        public int BackwardVertical { get; private set; }
        public bool NonlinearQuantizer { get; private set; }
        public bool AlternateIntraVlc { get; private set; }
        public bool AlternateScan { get; private set; }
        public bool ConcealmentVectors { get; private set; }
        public VideoScanMode ScanMode { get; private set; }
        public bool HasCodingExtension { get; private set; }

        public static Mpeg2PictureHeader Read(ReadOnlySpan<byte> payload)
        {
            Mpeg2BitReader bits = new(payload);
            bits.Skip(10);
            Mpeg2PictureKind kind = bits.Read(3) switch
            {
                1 => Mpeg2PictureKind.Intra,
                2 => Mpeg2PictureKind.Predictive,
                3 => Mpeg2PictureKind.Bidirectional,
                _ => throw new InvalidDataException("Invalid MPEG-2 picture coding type.")
            };
            bits.Skip(16);
            if (kind is Mpeg2PictureKind.Predictive or Mpeg2PictureKind.Bidirectional) { bits.Skip(4); }
            if (kind == Mpeg2PictureKind.Bidirectional) { bits.Skip(4); }
            while (bits.Flag()) { bits.Skip(8); }
            return new(kind);
        }

        public void ReadCodingExtension(scoped ref Mpeg2BitReader bits)
        {
            if (HasCodingExtension) { throw new InvalidDataException("Duplicate MPEG-2 picture coding extension."); }
            ForwardHorizontal = bits.Read(4);
            ForwardVertical = bits.Read(4);
            BackwardHorizontal = bits.Read(4);
            BackwardVertical = bits.Read(4);
            DcPrecision = bits.Read(2);
            int structure = bits.Read(2);
            bool topFirst = bits.Flag();
            bool framePrediction = bits.Flag();
            ConcealmentVectors = bits.Flag();
            NonlinearQuantizer = bits.Flag();
            AlternateIntraVlc = bits.Flag();
            AlternateScan = bits.Flag();
            bool repeatFirstField = bits.Flag();
            bits.Skip(1);
            bool progressive = bits.Flag();
            if (structure != 3 || !framePrediction || repeatFirstField)
            {
                throw new NotSupportedException("MPEG-2 decoding requires frame pictures with frame-based motion and DCT, without repeated fields.");
            }
            ScanMode = progressive ? VideoScanMode.Progressive : topFirst ? VideoScanMode.TopFieldFirst : VideoScanMode.BottomFieldFirst;
            HasCodingExtension = true;
        }
    }
}
