// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using Mediana.Core.Memory;

namespace Mediana.Codecs.H264
{
    internal partial class H264Picture : IDisposable
    {
        public const byte InterMode = 9;
        private readonly WritableMediaBuffer storage;
        private readonly int lumaBytes;
        private readonly int chromaBytes;
        private readonly int lumaBlocks;
        private readonly int chromaBlocks;
        private readonly int macroblocks;
        private readonly int referenceLists;
        private const int MotionBytesPerBlock = sizeof(byte) + 2 * sizeof(int);

        public H264Picture(H264SequenceParameterSet sequence, MediaBufferAllocator allocator, H264SliceKind kind,
            H264PictureStructure structure = H264PictureStructure.Frame)
        {
            referenceLists = kind switch
            {
                H264SliceKind.I or H264SliceKind.P => 1,
                H264SliceKind.B => 2,
                H264SliceKind.Sp or H264SliceKind.Si => throw new NotSupportedException("SP and SI picture storage is not supported."),
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };
            Sequence = sequence;
            Structure = structure;
            Layout = new H264PictureLayout(sequence, structure);
            LumaQuantizer = new H264Quantizer(sequence.LumaBitDepth);
            ChromaQuantizer = new H264Quantizer(sequence.ChromaBitDepth);
            SampleBytes = sequence.LumaBitDepth <= 8 ? 1 : 2;
            MaximumSample = (1 << sequence.LumaBitDepth) - 1;
            Width = sequence.CodedWidth;
            Height = sequence.CodedHeight / (Layout.IsField ? 2 : 1);
            int lumaSamples = checked(Width * Height);
            lumaBytes = checked(lumaSamples * SampleBytes);
            ChromaVerticalScale = sequence.ChromaFormat switch
            {
                1 => 2,
                2 or 3 => 1,
                _ => throw new NotSupportedException("H.264 picture storage requires 4:2:0, 4:2:2 or 4:4:4 chroma.")
            };
            ChromaHorizontalScale = sequence.ChromaFormat == 3 ? 1 : 2;
            ChromaMacroblockHeight = 16 / ChromaVerticalScale;
            ChromaMacroblockSamples = 16 / ChromaHorizontalScale * ChromaMacroblockHeight;
            MacroblockSamples = 256 + 2 * ChromaMacroblockSamples;
            LumaMacroblockBytes = 256 * SampleBytes;
            ChromaMacroblockBytes = ChromaMacroblockSamples * SampleBytes;
            MacroblockBytes = MacroblockSamples * SampleBytes;
            chromaBytes = lumaBytes / (ChromaHorizontalScale * ChromaVerticalScale);
            lumaBlocks = lumaSamples / 16;
            chromaBlocks = chromaBytes / (16 * SampleBytes);
            macroblocks = lumaSamples / 256;
            int metadataBytes = checked(lumaBlocks * (2 + MotionBytesPerBlock * referenceLists) + chromaBlocks * 2 + macroblocks * 2);
            int fieldPairBytes = Layout.MacroblockAdaptiveFrameField ? macroblocks / 2 : 0;
            storage = allocator.Allocate(checked(metadataBytes + fieldPairBytes));
            bool complete = false;
            try
            {
                Grid = new(Layout, sequence.WidthInMbs, storage.Memory.Slice(metadataBytes, fieldPairBytes));
                writablePixels = allocator.Allocate(checked(lumaBytes + chromaBytes * 2));
                pixelMemory = writablePixels.Memory;
                complete = true;
            }
            finally
            {
                if (!complete)
                {
                    storage.Dispose();
                }
            }
            Modes.Fill(byte.MaxValue);
            ReferenceIndices(H264ReferenceList.L0).Fill(byte.MaxValue);
            if (referenceLists == 2)
            {
                ReferenceIndices(H264ReferenceList.L1).Fill(byte.MaxValue);
            }
        }

        public H264SequenceParameterSet Sequence { get; }
        public H264PictureStructure Structure { get; }
        public H264PictureLayout Layout { get; }
        public H264MacroblockGrid Grid { get; }
        public H264Quantizer LumaQuantizer { get; }
        public H264Quantizer ChromaQuantizer { get; }
        public int Width { get; }
        public int Height { get; }
        public int ChromaVerticalScale { get; }
        public int ChromaHorizontalScale { get; }
        public bool FullResolutionChroma => ChromaHorizontalScale == 1;
        public int HorizontalScale(int plane) => plane == 0 ? 1 : ChromaHorizontalScale;
        public int SampleBytes { get; }
        public int LumaMacroblockBytes { get; }
        public int ChromaMacroblockBytes { get; }
        public int MacroblockBytes { get; }
        public int MaximumSample { get; }
        public int ChromaMacroblockHeight { get; }
        public int ChromaMacroblockSamples { get; }
        public int MacroblockSamples { get; }
        public int VerticalScale(int plane) => plane == 0 ? 1 : ChromaVerticalScale;
        public bool HasBidirectionalMotion => referenceLists == 2;
        public int FirstMacroblock { get; set; }
        private int ModesOffset => lumaBlocks + chromaBlocks * 2;
        public Span<byte> Modes => storage.Span.Slice(ModesOffset, lumaBlocks);
        private int QuantizersOffset => ModesOffset + lumaBlocks * (1 + MotionBytesPerBlock * referenceLists);
        public Span<byte> Quantizers => storage.Span.Slice(QuantizersOffset, macroblocks);
        public Span<byte> Transforms8x8 => storage.Span.Slice(QuantizersOffset + macroblocks, macroblocks);

        public Span<byte> Counts(int index) => index switch
        {
            0 => storage.Span[..lumaBlocks],
            1 => storage.Span.Slice(lumaBlocks, chromaBlocks),
            2 => storage.Span.Slice(lumaBlocks + chromaBlocks, chromaBlocks),
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };

        public int BlockIndex(int plane, int x, int y) => (y / 4) * (Stride(plane) / 4) + x / 4;
        public int Stride(int plane) => Width / HorizontalScale(plane);

        public int NeighbourCount(int plane, int x, int y)
        {
            if (Layout.MacroblockAdaptiveFrameField)
            {
                return MappedNeighbourCount(plane, x, y);
            }
            Span<byte> counts = Counts(plane);
            int index = BlockIndex(plane, x, y);
            int scale = HorizontalScale(plane);
            int verticalScale = VerticalScale(plane);
            bool hasLeft = IsInCurrentSlice(x * scale - 1, y * verticalScale);
            bool hasTop = IsInCurrentSlice(x * scale, y * verticalScale - 1);
            int left = hasLeft ? counts[index - 1] : 0;
            int top = hasTop ? counts[index - Stride(plane) / 4] : 0;
            return !hasLeft ? top : !hasTop ? left : (left + top + 1) >> 1;
        }

        public int PredictedMode(int x, int y, bool constrained = false)
        {
            if (Layout.MacroblockAdaptiveFrameField)
            {
                return MappedPredictedMode(x, y, constrained);
            }
            if (!IsInCurrentSlice(x - 1, y) || !IsInCurrentSlice(x, y - 1))
            {
                return 2;
            }
            int index = BlockIndex(0, x, y);
            int left = Modes[index - 1] & 127;
            int top = Modes[index - Width / 4] & 127;
            if (left == 127 || top == 127 || (constrained && (left == InterMode || top == InterMode)))
            {
                return 2;
            }
            return Math.Min(left == InterMode ? 2 : left, top == InterMode ? 2 : top);
        }

        public void SetLumaMode(int x, int y, int mode) => Modes[BlockIndex(0, x, y)] = (byte)(mode | 128);

        public void SetLuma8x8Mode(int x, int y, int mode)
        {
            int first = BlockIndex(0, x, y);
            Span<byte> modes = Modes;
            byte pending = (byte)(mode | 128);
            modes.Slice(first, 2).Fill(pending);
            modes.Slice(first + Width / 4, 2).Fill(pending);
        }

        public void WriteBlock(int plane, int x, int y, ReadOnlySpan<byte> prediction, ReadOnlySpan<int> residual) =>
            WriteBlock(plane, x, y, prediction, 4, residual, 4);

        public void Reconstruct4x4(int plane, int x, int y, ReadOnlySpan<byte> prediction, int predictionStride,
            ReadOnlySpan<int> levels, int qp, int? scaledDc = null, bool field = false)
        {
            if (Vector128.IsHardwareAccelerated && SampleBytes == 1 && !field)
            {
                (int offset, int stride) = SampleLocation(plane, x, y);
                if (H264InverseTransform.TryReconstructFrame4x4(levels, qp, scaledDc, prediction, predictionStride,
                    Plane(plane)[offset..], stride))
                {
                    if (plane == 0 || FullResolutionChroma)
                    {
                        Modes[BlockIndex(plane, x, y)] &= 127;
                    }
                    return;
                }
            }
            Span<int> residual = stackalloc int[16];
            H264InverseTransform.Residual4x4(levels, qp, residual, scaledDc,
                plane == 0 ? Sequence.LumaBitDepth : Sequence.ChromaBitDepth, field);
            WriteBlock(plane, x, y, prediction, predictionStride, residual, 4);
        }

        public void WriteLuma8x8(int x, int y, ReadOnlySpan<byte> prediction, int predictionStride, ReadOnlySpan<int> residual, int plane) =>
            WriteBlock(plane, x, y, prediction, predictionStride, residual, 8);

        private void WriteBlock(int plane, int x, int y, ReadOnlySpan<byte> prediction, int predictionStride, ReadOnlySpan<int> residual, int size)
        {
            (int offset, int stride) = SampleLocation(plane, x, y);
            Span<byte> pixels = Plane(plane);
            if (SampleBytes == 1)
            {
                H264SampleReconstruction.Write(prediction, predictionStride, residual, size, pixels[offset..], stride);
            }
            else
            {
                H264HighPrecisionReconstruction.Write(MemoryMarshal.Cast<byte, ushort>(prediction), predictionStride, residual, size,
                    MemoryMarshal.Cast<byte, ushort>(pixels)[offset..], stride, MaximumSample);
            }
            if (plane == 0 || FullResolutionChroma)
            {
                MarkReconstructed(BlockIndex(plane, x, y), Stride(plane) / 4, size / 4);
            }
        }

        public static (int X, int Y) LumaBlockPosition(int index) =>
            (((index / 4) % 2) * 8 + (index % 2) * 4, (index / 8) * 8 + ((index % 4) / 2) * 4);

        public void Dispose()
        {
            pixelMemory = default;
            writablePixels?.Dispose();
            publishedPixels?.Dispose();
            storage.Dispose();
        }

        public bool IsInCurrentSlice(int x, int y) => x >= 0 && x < Width && y >= 0 && y < Height &&
            y / 16 * Sequence.WidthInMbs + x / 16 >= FirstMacroblock;

        private bool IsAvailable(int x, int y, bool constrained)
        {
            if (!IsInCurrentSlice(x, y))
            {
                return false;
            }
            byte mode = Modes[BlockIndex(0, x, y)];
            return mode < 128 && (!constrained || mode != InterMode);
        }
    }
}
