using System.Buffers.Binary;
using System.IO.Compression;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Codecs.Png
{
    public static class PngEncoder
    {
        private const int ContainerBytes = 8 + 25 + 13 + 21 + 12 + 12;
        private static readonly VideoColorInfo Srgb = new(ColorPrimaries.Bt709, TransferFunction.Srgb,
            MatrixCoefficients.Rgb, ColorRange.Full, ChromaLocation.Unspecified);

        /// <summary>Encodes straight RGBA32 sRGB pixels as a non-interlaced 8-bit PNG using BCL zlib compression.
        /// Requires a known sample aspect ratio, stored as pHYs. Borrows input; returns an independent owned buffer.
        /// The allocation budget counts the complete reserved output array, including unused compression capacity.
        /// Compression workspace and object overhead are outside that budget.</summary>
        public static MediaBuffer Encode(VideoFrame input, MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(allocator);
            cancellationToken.ThrowIfCancellationRequested();
            if (!ReferenceEquals(input.Format.PixelFormat, VideoPixelFormat.Rgba32) || input.Format.Color != Srgb)
            {
                throw new NotSupportedException("PNG encoding requires straight RGBA32 pixels in full-range sRGB.");
            }
            SampleAspectRatio aspectRatio = input.Format.SampleAspectRatio
                ?? throw new NotSupportedException("PNG encoding requires a known sample aspect ratio.");
            long filteredBytes = checked(((long)input.Format.Width * 4 + 1) * input.Format.Height);
            int compressedCapacity = checked((int)(filteredBytes + (filteredBytes + 7) / 8 + (filteredBytes + 63) / 64 + 11));
            using WritableMediaBuffer writable = allocator.Allocate(checked(compressedCapacity + ContainerBytes));
            int offset = WriteHeaders(writable.Memory.Span, input.Format, aspectRatio, cancellationToken);
            "IDAT"u8.CopyTo(writable.Memory.Span.Slice(offset + 4, 4));
            int compressedLength;
            using (FixedMemoryWriteStream destination = new(writable.Memory.Slice(offset + 8, compressedCapacity)))
            {
                using (ZLibStream compressor = new(destination, CompressionLevel.Optimal, leaveOpen: true))
                {
                    ReadOnlySpan<byte> filter = [0];
                    for (int row = 0; row < input.Format.Height; row++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        compressor.Write(filter);
                        ReadOnlySpan<byte> pixels = input.GetRow(0, row);
                        while (!pixels.IsEmpty)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            int count = Math.Min(pixels.Length, 16 * 1024);
                            compressor.Write(pixels.Slice(0, count));
                            pixels = pixels.Slice(count);
                        }
                    }
                }
                compressedLength = destination.WrittenCount;
            }
            PngChunk.Finish(writable.Memory.Span.Slice(offset), compressedLength, cancellationToken);
            offset += compressedLength + 12;
            offset += PngChunk.Write(writable.Memory.Span.Slice(offset), "IEND"u8, [], cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            using MediaBuffer complete = writable.Publish();
            return complete.Slice(0, offset);
        }

        private static int WriteHeaders(Span<byte> output, VideoFormat format, SampleAspectRatio aspectRatio,
            CancellationToken cancellationToken)
        {
            ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
            signature.CopyTo(output);
            Span<byte> header = stackalloc byte[13];
            BinaryPrimitives.WriteInt32BigEndian(header, format.Width);
            BinaryPrimitives.WriteInt32BigEndian(header.Slice(4), format.Height);
            header[8] = 8;
            header[9] = 6;
            header.Slice(10).Clear();
            int offset = 8 + PngChunk.Write(output.Slice(8), "IHDR"u8, header, cancellationToken);
            offset += PngChunk.Write(output.Slice(offset), "sRGB"u8, [0], cancellationToken);
            Span<byte> physical = stackalloc byte[9];
            BinaryPrimitives.WriteInt32BigEndian(physical, aspectRatio.Denominator);
            BinaryPrimitives.WriteInt32BigEndian(physical.Slice(4), aspectRatio.Numerator);
            physical[8] = 0;
            offset += PngChunk.Write(output.Slice(offset), "pHYs"u8, physical, cancellationToken);
            return offset;
        }
    }
}
