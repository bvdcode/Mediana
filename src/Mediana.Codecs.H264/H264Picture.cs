// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using Mediana.Core.Memory;

namespace Mediana.Codecs.H264
{
    internal partial class H264Picture : IDisposable
    {
        public const byte InterMode = 9;
        private readonly int motionComponentBytes;
        private int MotionBytesPerBlock => sizeof(byte) + 2 * motionComponentBytes;
        private readonly WritableMediaBuffer storage;
        private readonly int modesOffset;
        private readonly int quantizersOffset;
        private readonly int lumaBlocks;
        private readonly int chromaBlocks;
        private readonly int macroblocks;
        private readonly int referenceLists;
        private readonly bool packedResiduals;
        private Memory<byte> metadata;
        private Memory<byte> lumaModes;

        public H264Picture(H264SequenceParameterSet sequence, MediaBufferAllocator allocator, H264SliceKind kind,
            H264PictureStructure structure = H264PictureStructure.Frame)
            : this(sequence, allocator, kind, structure, compactMotion: false)
        {
        }

        public static H264Picture CreateForCoding(H264SequenceParameterSet sequence, MediaBufferAllocator allocator,
            H264SliceKind kind, H264PictureStructure structure = H264PictureStructure.Frame, bool packedResiduals = false) =>
            new(sequence, allocator, kind, structure, compactMotion: true, packedResiduals);

        private H264Picture(H264SequenceParameterSet sequence, MediaBufferAllocator allocator, H264SliceKind kind,
            H264PictureStructure structure, bool compactMotion, bool packedResiduals = false)
        {
            this.packedResiduals = packedResiduals;
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
            motionComponentBytes = compactMotion && !Layout.MacroblockAdaptiveFrameField ? sizeof(short) : sizeof(int);
            LumaQuantizer = new H264Quantizer(sequence.LumaBitDepth);
            ChromaQuantizer = new H264Quantizer(sequence.ChromaBitDepth);
            SampleBytes = sequence.LumaBitDepth <= 8 ? 1 : 2;
            MaximumSample = (1 << sequence.LumaBitDepth) - 1;
            Width = sequence.CodedWidth;
            Height = sequence.CodedHeight / (Layout.IsField ? 2 : 1);
            int lumaSamples = checked(Width * Height);
            int lumaBytes = checked(lumaSamples * SampleBytes);
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
            int chromaBytes = lumaBytes / (ChromaHorizontalScale * ChromaVerticalScale);
            lumaBlocks = lumaSamples / 16;
            chromaBlocks = chromaBytes / (16 * SampleBytes);
            macroblocks = lumaSamples / 256;
            modesOffset = packedResiduals ? checked(macroblocks * sizeof(uint)) : checked(lumaBlocks + chromaBlocks * 2);
            int metadataBytes = checked(modesOffset + lumaBlocks * (1 + MotionBytesPerBlock * referenceLists) + macroblocks * 2);
            int fieldPairBytes = Layout.MacroblockAdaptiveFrameField ? macroblocks / 2 : 0;
            storage = allocator.Allocate(checked(metadataBytes + fieldPairBytes));
            bool complete = false;
            try
            {
                metadata = storage.Memory;
                lumaModes = metadata.Slice(modesOffset, lumaBlocks);
                quantizersOffset = modesOffset + lumaBlocks * (1 + MotionBytesPerBlock * referenceLists);
                Grid = new(Layout, sequence.WidthInMbs, metadata.Slice(metadataBytes, fieldPairBytes));
                writablePixels = allocator.Allocate(checked(lumaBytes + chromaBytes * 2));
                Memory<byte> pixels = writablePixels.Memory;
                lumaPixels = pixels[..lumaBytes];
                firstChromaPixels = pixels.Slice(lumaBytes, chromaBytes);
                secondChromaPixels = pixels.Slice(lumaBytes + chromaBytes, chromaBytes);
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
        public Span<byte> Modes
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                ObjectDisposedException.ThrowIf(lumaModes.IsEmpty, this);
                return lumaModes.Span;
            }
        }
        public Span<byte> Quantizers => Metadata.Slice(quantizersOffset, macroblocks);
        public Span<H264MacroblockFlags> MacroblockFlags =>
            MemoryMarshal.Cast<byte, H264MacroblockFlags>(Metadata.Slice(quantizersOffset + macroblocks, macroblocks));

        private Span<byte> Metadata
        {
            get
            {
                ObjectDisposedException.ThrowIf(metadata.IsEmpty, this);
                return metadata.Span;
            }
        }

        public bool Uses8x8Transform(int address) => MacroblockFlags[address].HasFlag(H264MacroblockFlags.Transform8x8);

        public void Set8x8Transform(int address, bool enabled)
        {
            ref H264MacroblockFlags flags = ref MacroblockFlags[address];
            flags = enabled ? flags | H264MacroblockFlags.Transform8x8 : flags & ~H264MacroblockFlags.Transform8x8;
        }

        public Span<byte> Counts(int index)
        {
            if (packedResiduals)
            {
                throw new InvalidOperationException("This H.264 picture stores coded block flags.");
            }
            return index switch
            {
                0 => Metadata[..lumaBlocks],
                1 => Metadata.Slice(lumaBlocks, chromaBlocks),
                2 => Metadata.Slice(lumaBlocks + chromaBlocks, chromaBlocks),
                _ => throw new ArgumentOutOfRangeException(nameof(index))
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

        internal void ReconstructLuma8x8(int plane, int x, int y, ReadOnlySpan<byte> prediction,
            int predictionStride, ReadOnlySpan<int> coefficients, int qp, bool field)
        {
            if (!field && TryWriteInverse8x8(plane, x, y, prediction, predictionStride, coefficients, qp))
            {
                return;
            }

            Span<int> residual = stackalloc int[64];
            H264InverseTransform8x8.Residual(coefficients, qp, residual, Sequence.LumaBitDepth, field: field);
            WriteLuma8x8(x, y, prediction, predictionStride, residual, plane);
        }

        private bool TryWriteInverse8x8(int plane, int x, int y, ReadOnlySpan<byte> prediction,
            int predictionStride, ReadOnlySpan<int> coefficients, int qp)
        {
            if (SampleBytes != 1 || Layout.MacroblockAdaptiveFrameField || !H264VectorInverse8x8.IsSupported)
            {
                return false;
            }
            (int offset, int stride) = SampleLocation(plane, x, y);
            if (!H264VectorInverse8x8.TryWrite(coefficients, qp, prediction, predictionStride, Plane(plane)[offset..], stride))
            {
                return false;
            }
            if (plane == 0 || FullResolutionChroma)
            {
                MarkReconstructed(BlockIndex(plane, x, y), Stride(plane) / 4, 2);
            }
            return true;
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
            lumaPixels = default;
            firstChromaPixels = default;
            secondChromaPixels = default;
            metadata = default;
            lumaModes = default;
            writablePixels?.Dispose();
            publishedPixels?.Dispose();
            storage.Dispose();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsInCurrentSlice(int x, int y) =>
            (uint)x < (uint)Width && (uint)y < (uint)Height &&
            (FirstMacroblock == 0 || (y >> 4) * Sequence.WidthInMbs + (x >> 4) >= FirstMacroblock);

        private bool IsAvailable(int x, int y, bool constrained)
        {
            if (!IsInCurrentSlice(x, y))
            {
                return false;
            }
            byte mode = Modes[(y >> 2) * (Width >> 2) + (x >> 2)];
            return mode < 128 && (!constrained || mode != InterMode);
        }
    }
}
