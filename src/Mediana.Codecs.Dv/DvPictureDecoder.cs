// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Formats.Video;

namespace Mediana.Codecs.Dv
{
    internal static class DvPictureDecoder
    {
        private const int Width = DvFrameHeader.Width;
        private const int Height = DvFrameHeader.Height;
        private const int BlockBytes = DvFrameHeader.DifBlockBytes;
        private const int SequenceBytes = DvFrameHeader.SequenceBlocks * BlockBytes;
        private const int VideoSegments = 27;
        private const int VideoSegmentBytes = 5 * BlockBytes;
        private const int LumaBytes = Width * Height;
        private const int ChromaBytes = LumaBytes / 4;
        private static readonly VideoColorInfo Color = new(Matrix: MatrixCoefficients.Bt601,
            Range: ColorRange.Limited, ChromaLocation: ChromaLocation.TopLeft);
        private static readonly PlaneLayout[] Planes =
        [
            new(0, Width, Width, Height),
            new(LumaBytes, Width / 2, Width / 2, Height / 2),
            new(LumaBytes + ChromaBytes, Width / 2, Width / 2, Height / 2)
        ];

        internal static VideoFrame Decode(MediaPacket packet, EncodedStreamInfo stream, MediaBufferAllocator allocator,
            CancellationToken cancellationToken)
        {
            ReadOnlySpan<byte> data = packet.Data.Span;
            DvFrameHeader header = DvFrameHeader.Read(data);
            using WritableMediaBuffer storage = allocator.Allocate(LumaBytes + 2 * ChromaBytes);
            Span<byte> output = storage.Span;
            for (int sequence = 0; sequence < DvFrameHeader.SequenceCount; sequence++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ReadOnlySpan<byte> blocks = data.Slice(sequence * SequenceBytes, SequenceBytes);
                if ((blocks[0] >> 5) != 0 || (blocks[1] >> 4) != sequence || blocks[2] != 0)
                {
                    throw new InvalidDataException("A DV DIF sequence header is out of order.");
                }
                for (int segment = 0; segment < VideoSegments; segment++)
                {
                    int block = 7 + segment * 5 + segment / 3;
                    DvVideoSegment.Decode(blocks.Slice(block * BlockBytes, VideoSegmentBytes), output, sequence, segment);
                }
            }

            SampleAspectRatio aspect = stream.AspectRatioDescription?.Resolve(header.SampleAspectRatio) ?? header.SampleAspectRatio;
            VideoFormat format = new(Width, Height, VideoPixelFormat.Yuv420P8, Color, aspect, stream.ColorDescription, header.ScanMode);
            using MediaBuffer image = storage.Publish();
            return new(format, image, Planes, packet.PresentationTime ?? packet.DecodeTime, packet.Duration);
        }
    }
}
