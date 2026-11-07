// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core.Memory;
using Mediana.Formats.Video;

namespace Mediana.Codecs.Mpeg4Visual
{
    internal partial class Mpeg4PictureDecoder : IDisposable
    {
        private readonly Mpeg4VisualConfiguration config;
        private readonly MediaBufferAllocator allocator;
        private readonly int mbWidth, mbHeight, lumaSize, blockCount;
        private WritableMediaBuffer? predictionStorage, motionStorage, referenceMotion;
        private MediaBuffer? previous, future;
        private readonly Mpeg4VisualClock clock = new();
        internal int Width { get; }
        internal int Height { get; }
        internal long PictureTime { get; private set; }
        private Span<int> Predictions => MemoryMarshal.Cast<byte, int>(predictionStorage!.Span);
        private Span<int> Motion => MemoryMarshal.Cast<byte, int>(motionStorage!.Span);
        private Span<int> ReferenceMotion => MemoryMarshal.Cast<byte, int>(referenceMotion!.Span);
        internal Mpeg4PictureDecoder(Mpeg4VisualConfiguration configuration, MediaBufferAllocator allocator)
        {
            config = configuration; this.allocator = allocator;
            mbWidth = (config.Width + 15) / 16; mbHeight = (config.Height + 15) / 16;
            Width = mbWidth * 16; Height = mbHeight * 16; lumaSize = checked(Width * Height); blockCount = checked(mbWidth * mbHeight * 6);
            if (config.DataPartitioned || config.NewPrediction || config.QuantizerBits != 5)
            { throw new NotSupportedException("Partitioned, new-prediction or extended-quantizer MPEG-4 Visual layers are not supported."); }
            bool success = false;
            try
            {
                predictionStorage = allocator.Allocate(checked(blockCount * 16 * sizeof(int)));
                motionStorage = allocator.Allocate(checked(mbWidth * mbHeight * 10 * sizeof(int)));
                referenceMotion = allocator.Allocate(motionStorage.Memory.Length);
                success = true;
            }
            finally { if (!success) { Dispose(); } }
        }
        internal void ReadGroup(ReadOnlySpan<byte> data) => clock.ReadGroup(data);
        internal MediaBuffer? Decode(ReadOnlySpan<byte> payload, out Mpeg4VopHeader header, CancellationToken token)
        {
            Mpeg4VisualBitReader bits = new(payload); header = Mpeg4VopHeader.Read(ref bits, config);
            long pictureTime = clock.Read(header, config);
            PictureTime = pictureTime;
            if (!header.IsCoded)
            { return future?.Retain() ?? throw new InvalidDataException("An uncoded MPEG-4 Visual picture has no preceding reference."); }
            if (header.Type != Mpeg4VopType.Intra && future is null) { throw new InvalidDataException("MPEG-4 Visual decoding must start with an intra picture."); }
            if (header.Type == Mpeg4VopType.Bidirectional && previous is null)
            {
                if (pictureTime < clock.LastReferenceTime) { return null; }
                throw new InvalidDataException("The MPEG-4 Visual B picture has no past reference.");
            }
            if (config.Interlaced) { throw new NotSupportedException("Interlaced MPEG-4 Visual pictures are not supported."); }
            if (header.Type != Mpeg4VopType.Bidirectional) { Predictions.Clear(); Motion.Clear(); }
            using WritableMediaBuffer picture = allocator.Allocate(checked(lumaSize + lumaSize / 2));
            if (header.Type == Mpeg4VopType.Bidirectional) { DecodeBidirectional(ref bits, header, picture.Span, pictureTime, token); }
            else { DecodeMacroblocks(ref bits, header, picture.Span, pictureTime, token); }
            MediaBuffer result = picture.Publish();
            if (header.Type != Mpeg4VopType.Bidirectional)
            {
                previous?.Dispose(); previous = future; future = result.Retain();
                (referenceMotion, motionStorage) = (motionStorage, referenceMotion);
            }
            return result;
        }
        private void DecodeMacroblocks(ref Mpeg4VisualBitReader bits, Mpeg4VopHeader header, Span<byte> picture, long pictureTime, CancellationToken token)
        {
            int quantizer = header.Quantizer, bound = 0;
            Span<int> coefficients = stackalloc int[64];
            for (int mb = 0; mb < mbWidth * mbHeight; mb++)
            {
                token.ThrowIfCancellationRequested();
                int stuffing = header.Type == Mpeg4VopType.Intra ? 9 : 10;
                while (bits.RemainingBits >= stuffing && bits.Peek(stuffing) == 1) { bits.Skip(stuffing); }
                ReadResynchronization(ref bits, header, mb, ref quantizer, ref bound);
                bool skipped = header.Type != Mpeg4VopType.Intra && bits.Flag();
                int cbp = 0, mode = 0; bool acPrediction = false;
                if (!skipped)
                {
                    int symbol = header.Type == Mpeg4VopType.Intra ? Mpeg4Codebooks.IntraMacro.Read(ref bits) : Mpeg4Codebooks.InterMacro.Read(ref bits);
                    if (header.Type == Mpeg4VopType.Intra) { mode = symbol < 4 ? 3 : 4; }
                    else
                    {
                        mode = (symbol / 4) switch { 0 => 0, 1 => 3, 2 => 1, 3 => 4, 4 => 2,
                            _ => throw new InvalidDataException("The MPEG-4 Visual macroblock type is invalid.") };
                    }
                    bool intra = mode is 3 or 4;
                    if (intra) { acPrediction = bits.Flag(); }
                    int cbpy = Mpeg4Codebooks.Cbpy.Read(ref bits);
                    if (!intra) { cbpy ^= 15; } cbp = (cbpy << 2) | (symbol & 3);
                    if (mode is 1 or 4)
                    { ReadOnlySpan<int> changes = [-1,-2,1,2]; quantizer = Math.Clamp(quantizer + changes[bits.Read(2)], 1, 31); }
                    if (!intra) { ReadMotion(ref bits, mb, mode == 2, header.ForwardCode, bound); }
                }
                Motion[mb * 10 + 8] = skipped ? 16 : mode; Motion[mb * 10 + 9] = quantizer;
                if (mode is not 3 and not 4) { Predict(picture, future!.Memory.Span, mb, Motion.Slice(mb * 10, 8), mode == 2, header.Rounding, false); }
                for (int block = 0; block < 6; block++)
                {
                    coefficients.Clear(); bool intra = mode is 3 or 4; ulong nonzero = 0;
                    if (intra) { nonzero = ReadIntra(ref bits, coefficients, mb, block, quantizer, acPrediction, cbp, header, bound); }
                    else if ((cbp & (1 << (5 - block))) != 0) { nonzero = ReadResidual(ref bits, coefficients, false, 0, header.AlternateScan ? Mpeg4Scan.Vertical : Mpeg4Scan.Zigzag); }
                    if (intra || (cbp & (1 << (5 - block))) != 0)
                    {
                        nonzero = Dequantize(coefficients, quantizer, intra, DcScale(quantizer, block < 4), nonzero);
                        (int offset, int stride) = BlockAddress(mb, block);
                        Mpeg4InverseDct.Write(coefficients, picture[offset..], stride, intra, nonzero);
                    }
                }
            }
        }
        private (int Offset, int Stride) BlockAddress(int mb, int block)
        {
            int x = mb % mbWidth, y = mb / mbWidth;
            if (block < 4) { return ((y * 16 + (block >> 1) * 8) * Width + x * 16 + (block & 1) * 8, Width); }
            return (lumaSize + (block - 4) * (lumaSize / 4) + y * 8 * (Width / 2) + x * 8, Width / 2);
        }
        public void Dispose()
        {
            previous?.Dispose(); future?.Dispose(); predictionStorage?.Dispose(); motionStorage?.Dispose(); referenceMotion?.Dispose();
            previous = future = null; predictionStorage = motionStorage = referenceMotion = null;
        }
    }
}
