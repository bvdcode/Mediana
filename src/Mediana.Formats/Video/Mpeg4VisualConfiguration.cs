// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Formats.Video
{
    /// <summary>Rectangular MPEG-4 Visual object-layer configuration.</summary>
    public class Mpeg4VisualConfiguration
    {
        public const string CodecId = "mpeg4";
        private Mpeg4VisualConfiguration() { }
        public int Width { get; private set; }
        public int Height { get; private set; }
        public int Version { get; private set; }
        public int ObjectType { get; private set; }
        public int TimeIncrementResolution { get; private set; }
        public int TimeIncrementBits => Math.Max(1, BitOperations.Log2((uint)(TimeIncrementResolution - 1)) + 1);
        public int? FixedTimeIncrement { get; private set; }
        public int AspectNumerator { get; private set; } = 1;
        public int AspectDenominator { get; private set; } = 1;
        public bool LowDelay { get; private set; }
        public bool Interlaced { get; private set; }
        public int SpriteMode { get; private set; }
        public int SpritePoints { get; private set; }
        public int SpriteAccuracy { get; private set; }
        public bool SpriteBrightnessChange { get; private set; }
        public bool QuarterPel { get; private set; }
        public bool ResyncMarkerDisabled { get; private set; }
        public bool DataPartitioned { get; private set; }
        public bool ReversibleVlc { get; private set; }
        public bool NewPrediction { get; private set; }
        public bool ReducedResolution { get; private set; }
        public bool MpegQuantization { get; private set; }
        public int QuantizerBits { get; private set; } = 5;
        public ReadOnlyMemory<byte> IntraMatrix => intra;
        public ReadOnlyMemory<byte> InterMatrix => inter;
        private readonly byte[] intra = [8, 17, 18, 19, 21, 23, 25, 27, 17, 18, 19, 21, 23, 25, 27, 28, 20, 21, 22, 23, 24, 26, 28, 30, 21, 22, 23, 24, 26, 28, 30, 32, 22, 23, 24, 26, 28, 30, 32, 35, 23, 24, 26, 28, 30, 32, 35, 38, 25, 26, 28, 30, 32, 35, 38, 41, 27, 28, 30, 32, 35, 38, 41, 45];
        private readonly byte[] inter = [16, 17, 18, 19, 20, 21, 22, 23, 17, 18, 19, 20, 21, 22, 23, 24, 18, 19, 20, 21, 22, 23, 24, 25, 19, 20, 21, 22, 23, 24, 26, 27, 20, 21, 22, 23, 25, 26, 27, 28, 21, 22, 23, 24, 26, 27, 28, 30, 22, 23, 24, 26, 27, 28, 30, 31, 23, 24, 25, 27, 28, 30, 31, 33];
        public static Mpeg4VisualConfiguration Read(ReadOnlySpan<byte> configuration)
        {
            int position = Mpeg4VisualStartCodes.Find(configuration);
            while (position >= 0)
            {
                int next = Mpeg4VisualStartCodes.Find(configuration, position + 4);
                if (configuration[position + 3] is >= 0x20 and <= 0x2f)
                { return ReadLayer(configuration.Slice(position + 4, (next < 0 ? configuration.Length : next) - position - 4)); }
                position = next;
            }
            throw new InvalidDataException("The MPEG-4 Visual object-layer header is missing.");
        }
        private static Mpeg4VisualConfiguration ReadLayer(ReadOnlySpan<byte> data)
        {
            Mpeg4VisualBitReader bits = new(data); Mpeg4VisualConfiguration result = new();
            bits.Skip(1); result.ObjectType = bits.Read(8);
            if (bits.Flag()) { result.Version = bits.Read(4); bits.Skip(3); }
            else { result.Version = 1; }
            int aspect = bits.Read(4);
            (result.AspectNumerator, result.AspectDenominator) = aspect switch
            {
                1 => (1, 1),
                2 => (12, 11),
                3 => (10, 11),
                4 => (16, 11),
                5 => (40, 33),
                15 => (bits.Read(8), bits.Read(8)),
                _ => throw new InvalidDataException("The MPEG-4 Visual aspect ratio identifier is reserved.")
            };
            if (result.AspectNumerator == 0 || result.AspectDenominator == 0) { throw new InvalidDataException("The MPEG-4 Visual aspect ratio must be positive."); }
            if (bits.Flag())
            {
                if (bits.Read(2) != 1) { throw new NotSupportedException("The MPEG-4 Visual chroma format is not 4:2:0."); }
                result.LowDelay = bits.Flag();
                if (bits.Flag())
                {
                    bits.Skip(15); bits.Marker(); bits.Skip(15); bits.Marker(); bits.Skip(15); bits.Marker();
                    bits.Skip(3); bits.Skip(11); bits.Marker(); bits.Skip(15); bits.Marker();
                }
            }
            if (bits.Read(2) != 0) { throw new NotSupportedException("The MPEG-4 Visual object shape is not rectangular."); }
            bits.Marker(); result.TimeIncrementResolution = bits.Read(16); bits.Marker();
            if (result.TimeIncrementResolution == 0) { throw new InvalidDataException("The MPEG-4 Visual clock resolution must be positive."); }
            if (bits.Flag())
            {
                result.FixedTimeIncrement = bits.Read(result.TimeIncrementBits);
                if (result.FixedTimeIncrement == 0) { throw new InvalidDataException("The MPEG-4 Visual fixed increment is invalid."); }
            }
            bits.Marker(); result.Width = bits.Read(13); bits.Marker(); result.Height = bits.Read(13); bits.Marker();
            if (result.Width == 0 || result.Height == 0) { throw new InvalidDataException("The MPEG-4 Visual dimensions must be positive."); }
            result.Interlaced = bits.Flag();
            if (!bits.Flag()) { throw new NotSupportedException("Overlapped MPEG-4 Visual block motion is not supported."); }
            result.SpriteMode = bits.Read(result.Version == 1 ? 1 : 2);
            if (result.SpriteMode is 1 or 2)
            {
                if (result.SpriteMode == 1)
                { bits.Skip(13); bits.Marker(); bits.Skip(13); bits.Marker(); bits.Skip(13); bits.Marker(); bits.Skip(13); bits.Marker(); }
                result.SpritePoints = bits.Read(6); result.SpriteAccuracy = bits.Read(2); result.SpriteBrightnessChange = bits.Flag();
                if (result.SpriteMode == 1) { bits.Skip(1); }
            }
            else if (result.SpriteMode != 0) { throw new InvalidDataException("The MPEG-4 Visual sprite mode is reserved."); }
            if (bits.Flag())
            {
                result.QuantizerBits = bits.Read(4);
                if (bits.Read(4) != 8 || result.QuantizerBits is < 3 or > 9) { throw new NotSupportedException("The MPEG-4 Visual sample precision is not 8-bit."); }
            }
            result.MpegQuantization = bits.Flag();
            if (result.MpegQuantization)
            {
                if (bits.Flag()) { ReadMatrix(ref bits, result.intra); }
                if (bits.Flag()) { ReadMatrix(ref bits, result.inter); }
            }
            result.QuarterPel = result.Version != 1 && bits.Flag();
            if (!bits.Flag()) { throw new NotSupportedException("MPEG-4 Visual complexity-estimation fields are not supported."); }
            result.ResyncMarkerDisabled = bits.Flag(); result.DataPartitioned = bits.Flag();
            result.ReversibleVlc = result.DataPartitioned && bits.Flag();
            if (result.Version != 1)
            {
                result.NewPrediction = bits.Flag();
                if (result.NewPrediction) { bits.Skip(3); }
                result.ReducedResolution = bits.Flag();
            }
            if (bits.Flag()) { throw new NotSupportedException("Scalable MPEG-4 Visual object layers are not supported."); }
            return result;
        }
        private static void ReadMatrix(ref Mpeg4VisualBitReader bits, Span<byte> matrix)
        {
            ReadOnlySpan<byte> scan = [0, 1, 8, 16, 9, 2, 3, 10, 17, 24, 32, 25, 18, 11, 4, 5, 12, 19, 26, 33, 40, 48, 41, 34, 27, 20, 13, 6, 7, 14, 21, 28, 35, 42, 49, 56, 57, 50, 43, 36, 29, 22, 15, 23, 30, 37, 44, 51, 58, 59, 52, 45, 38, 31, 39, 46, 53, 60, 61, 54, 47, 55, 62, 63];
            byte last = 0;
            for (int i = 0; i < 64; i++)
            {
                byte value = (byte)bits.Read(8);
                if (value == 0)
                {
                    if (i == 0) { throw new InvalidDataException("An MPEG-4 Visual quantization matrix starts with zero."); }
                    for (; i < 64; i++) { matrix[scan[i]] = last; }
                    return;
                }
                matrix[scan[i]] = last = value;
            }
        }
    }
}
