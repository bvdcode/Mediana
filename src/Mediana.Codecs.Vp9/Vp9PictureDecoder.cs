// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Mediana.Core.Memory;

namespace Mediana.Codecs.Vp9
{
    internal class Vp9PictureDecoder(MediaBufferAllocator allocator) : IDisposable
    {
        private readonly Vp9HeaderState headers = new();
        private readonly Vp9ProbabilityState probabilities = new();
        private readonly Vp9CoefficientCounts coefficients = new();
        private readonly Vp9SymbolCounts symbols = new();
        private readonly Vp9ReferencePictures references = new();
        private WritableMediaBuffer? priorBlocks, currentBlocks;
        private Vp9FrameHeader? previous;

        internal (Vp9FrameHeader Header, Vp9Picture Picture) Decode(ReadOnlyMemory<byte> encoded, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Vp9FrameHeader header = Vp9FrameHeader.Parse(encoded.Span, headers);
            if (header.ShowExisting) { return (header, references.Get(header.ExistingReference).Retain()); }
            probabilities.Begin(header); coefficients.Clear(); symbols.Clear();
            Vp9CompressedHeader compressed = Vp9CompressedHeader.Parse(encoded.Span.Slice(header.CompressedOffset, header.CompressedLength), header, probabilities.Working);
            int sampleCount = checked(header.SuperblockColumns * 64 * header.SuperblockRows * 64);
            sampleCount = checked(sampleCount + 2 * (sampleCount >> ((header.Geometry.SubsamplingX ? 1 : 0) + (header.Geometry.SubsamplingY ? 1 : 0))));
            int blockBytes = checked(header.SuperblockColumns * 8 * header.SuperblockRows * 8 * Unsafe.SizeOf<Vp9BlockInfo>());
            if (currentBlocks is null || currentBlocks.Memory.Length != blockBytes)
            { currentBlocks?.Dispose(); currentBlocks = null; currentBlocks = allocator.Allocate(blockBytes); }
            bool sameSize = previous is not null && header.Geometry.Width == previous.Geometry.Width && header.Geometry.Height == previous.Geometry.Height;
            using WritableMediaBuffer output = allocator.Allocate(checked(sampleCount * (header.Geometry.BitDepth == 8 ? 1 : 2)));
            using (MediaBufferWorkspace workspace = new(allocator))
            {
                ReadOnlySpan<Vp9BlockInfo> prior = sameSize && !header.ErrorResilient && priorBlocks is not null ? MemoryMarshal.Cast<byte, Vp9BlockInfo>(priorBlocks.Span) : default;
                Span<Vp9BlockInfo> blocks = MemoryMarshal.Cast<byte, Vp9BlockInfo>(currentBlocks.Span);
                if (header.Geometry.BitDepth == 8) { Reconstruct<byte>(encoded, header, compressed, output.Span, workspace, prior, blocks, cancellationToken); }
                else { Reconstruct<ushort>(encoded, header, compressed, output.Span, workspace, prior, blocks, cancellationToken); }
            }
            probabilities.End(header, coefficients, symbols); headers.Commit(header);
            (priorBlocks, currentBlocks) = (currentBlocks, priorBlocks); previous = header;
            Vp9Picture picture = new(header.Geometry, output.Publish());
            bool transferred = false;
            try { references.Update(header, picture); transferred = true; return (header, picture); }
            finally { if (!transferred) { picture.Dispose(); } }
        }

        private void Reconstruct<T>(ReadOnlyMemory<byte> encoded, Vp9FrameHeader header, Vp9CompressedHeader compressed,
            Span<byte> output, MediaBufferWorkspace workspace, ReadOnlySpan<Vp9BlockInfo> prior, Span<Vp9BlockInfo> blocks, CancellationToken cancellationToken) where T : unmanaged, IBinaryInteger<T>
        {
            bool usePreviousMotion = !prior.IsEmpty && previous is not null && previous.ShowFrame && !header.IsIntra;
            Vp9ImageDecoder<T> decoder = new(header, headers, compressed, probabilities.Working, output, workspace, coefficients,
                cancellationToken, symbols, references, prior, blocks, usePreviousMotion);
            decoder.Decode(encoded[(header.CompressedOffset + header.CompressedLength)..]); decoder.Filter();
        }

        public void Dispose()
        {
            references.Dispose(); priorBlocks?.Dispose(); currentBlocks?.Dispose(); priorBlocks = null; currentBlocks = null;
        }
    }
}
