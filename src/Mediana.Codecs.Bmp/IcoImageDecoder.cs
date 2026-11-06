// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Codecs.Png;
using Mediana.Core.Memory;
using Mediana.Core.Metadata;
using Mediana.Core.Video;

namespace Mediana.Codecs.Bmp
{
    /// <summary>Reads ICO/CUR entries in directory order. Page selection is explicit and zero-based.
    /// PNG entries retain their PNG interpretation; DIB entries use icon alpha and AND masks.</summary>
    public class IcoImageDecoder : ISynchronousImageDecoder, IImageInfoReader
    {
        public string ImplementationId => "ico.managed.image";
        public int ProbeLength => 6;
        public ImageDecoderFeatures Features => ImageDecoderFeatures.Pages;
        public bool CanDecode(ReadOnlySpan<byte> prefix, long? encodedLength = null) =>
            prefix.Length >= 6 && prefix[0] == 0 && prefix[1] == 0 && prefix[2] is 1 or 2 && prefix[3] == 0;
        private static ReadOnlyMemory<byte> Entry(ReadOnlyMemory<byte> encoded, int number, out int count, out int width, out int height)
        {
            ReadOnlySpan<byte> data = encoded.Span;
            if (data.Length < 6 || data[0] != 0 || data[1] != 0 || data[2] is not (1 or 2) || data[3] != 0)
            { throw new InvalidDataException("Invalid icon directory."); }
            count = BinaryPrimitives.ReadUInt16LittleEndian(data[4..]);
            if (count == 0 || count > (data.Length - 6) / 16) { throw new InvalidDataException("Truncated icon directory."); }
            if ((uint)number >= count) { throw new ArgumentOutOfRangeException(nameof(number)); }
            int entry = 6 + number * 16;
            width = data[entry] == 0 ? 256 : data[entry]; height = data[entry + 1] == 0 ? 256 : data[entry + 1];
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(data[(entry + 8)..]);
            uint offset = BinaryPrimitives.ReadUInt32LittleEndian(data[(entry + 12)..]);
            if (offset < 6 + count * 16 || offset > data.Length || length > data.Length - offset)
            { throw new InvalidDataException("Icon entry exceeds its file."); }
            return encoded.Slice((int)offset, checked((int)length));
        }
        public async ValueTask<ImageInfo> ReadInfoAsync(ReadOnlyMemory<byte> encoded, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadOnlyMemory<byte> data = Entry(encoded, 0, out int count, out int width, out int height);
            List<MediaMetadata> pages = [];
            for (int i = 0; i < count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ReadOnlyMemory<byte> item = Entry(encoded, i, out _, out int w, out int h);
                pages.Add(new("entry/" + i, [new("ico", "dimensions", w, h), new("ico", "encoded_bytes", item.Length)]));
            }
            bool png = data.Span.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
            ImageInfo first = png ? await new PngImageDecoder().ReadInfoAsync(data, cancellationToken).ConfigureAwait(false)
                : BmpImageDecoder.Inspect(new(data, -1, true), "ico");
            if (first.Width != width || first.Height != height) { throw new InvalidDataException("Icon entry dimensions disagree with its directory."); }
            return new(encoded.Span[2] == 2 ? "cur" : "ico", width, height, ImageDimensionUnit.Pixels, count,
                metadata: new("image", children: [first.Metadata, .. pages]));
        }
        public ValueTask<VideoFrame> DecodeAsync(ReadOnlyMemory<byte> encoded, ImageDecodeRequest request,
            MediaBufferAllocator allocator, CancellationToken cancellationToken = default) => ValueTask.FromResult(Decode(encoded, request, allocator, cancellationToken));
        public VideoFrame Decode(ReadOnlyMemory<byte> encoded, ImageDecodeRequest request,
            MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(allocator);
            request.Validate(Features); cancellationToken.ThrowIfCancellationRequested();
            ReadOnlyMemory<byte> data = Entry(encoded, request.PageIndex, out _, out int width, out int height);
            ImageDecodeRequest nested = request with { PageIndex = 0 };
            VideoFrame frame = data.Span.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
                ? new PngImageDecoder(new() { MaximumPixels = request.MaximumPixels ?? Array.MaxLength / 4, MaximumEncodedBytes = Array.MaxLength }).Decode(data, nested, allocator, cancellationToken)
                : BmpImageDecoder.DecodeDib(new(data, -1, true), nested, allocator, cancellationToken);
            if (frame.Format.Width != width || frame.Format.Height != height)
            { frame.Dispose(); throw new InvalidDataException("Icon pixels disagree with the directory dimensions."); }
            return frame;
        }
    }
}
