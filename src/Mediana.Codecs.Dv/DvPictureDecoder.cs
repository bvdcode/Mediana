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
        private const int BlockBytes = DvFrameHeader.DifBlockBytes;
        private const int SequenceBytes = DvFrameHeader.SequenceBlocks * BlockBytes;
        private const int VideoSegments = 27;
        private const int VideoSegmentBytes = 5 * BlockBytes;
        private static readonly VideoColorInfo PalColor = new(Matrix: MatrixCoefficients.Bt601,
            Range: ColorRange.Limited, ChromaLocation: ChromaLocation.TopLeft);
        private static readonly VideoColorInfo NtscColor = PalColor with { ChromaLocation = ChromaLocation.Left };

        internal static VideoColorInfo GetColor(DvFrameHeader header) => header.IsNtsc ? NtscColor : PalColor;

        internal static VideoFrame Decode(MediaPacket packet, EncodedStreamInfo stream, MediaBufferAllocator allocator,
            CancellationToken cancellationToken)
        {
            ReadOnlySpan<byte> data = packet.Data.Span;
            DvFrameHeader header = DvFrameHeader.Read(data);
            SampleAspectRatio aspect = stream.AspectRatioDescription?.Resolve(header.SampleAspectRatio) ?? header.SampleAspectRatio;
            VideoPixelFormat pixelFormat = header.IsNtsc ? VideoPixelFormat.Yuv411P8 : VideoPixelFormat.Yuv420P8;
            VideoFormat format = new(Width, header.CodedHeight, pixelFormat, GetColor(header),
                aspect, stream.ColorDescription, header.ScanMode);
            PlaneLayout[] planes = format.CreateTightLayout();
            int lumaBytes = Width * header.CodedHeight;
            using WritableMediaBuffer storage = allocator.Allocate(lumaBytes + lumaBytes / 2);
            Span<byte> output = storage.Span;
            for (int sequence = 0; sequence < header.DifSequences; sequence++)
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
                    DvVideoSegment.Decode(blocks.Slice(block * BlockBytes, VideoSegmentBytes), output, sequence, segment, header.IsNtsc);
                }
            }

            using MediaBuffer image = storage.Publish();
            return new(format, image, planes, packet.PresentationTime ?? packet.DecodeTime, packet.Duration);
        }
    }
}
