// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Codecs;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Images.Heif
{
    /// <summary>Registered HEIC primary-image decoding. HEIF transforms are applied by the document decoder;
    /// pixel aspect is an explicit caller interpretation, and ICC conversion remains unsupported.</summary>
    public class HeicImageDecoder : IImageDecoder
    {
        private readonly SampleAspectRatio sampleAspectRatio;
        private readonly HeicDecodeOptions options;

        public HeicImageDecoder(SampleAspectRatio sampleAspectRatio, HeicDecodeOptions? options = null)
        {
            sampleAspectRatio.Validate();
            this.sampleAspectRatio = sampleAspectRatio;
            this.options = options ?? HeicDecodeOptions.Default;
        }

        public string ImplementationId => "heif.managed.image";
        public int ProbeLength => 512;
        public ImageDecoderFeatures Features => ImageDecoderFeatures.SingleImage;

        public bool CanDecode(ReadOnlySpan<byte> prefix)
        {
            if (prefix.Length < 16 || !prefix.Slice(4, 4).SequenceEqual("ftyp"u8))
            {
                return false;
            }
            uint size = BinaryPrimitives.ReadUInt32BigEndian(prefix);
            int header = 8;
            ulong declaredSize = size;
            if (size == 1)
            {
                if (prefix.Length < 24)
                {
                    return false;
                }
                declaredSize = BinaryPrimitives.ReadUInt64BigEndian(prefix[8..]);
                header = 16;
            }
            if (size == 0)
            {
                declaredSize = (ulong)prefix.Length;
            }
            if (declaredSize < (ulong)header + 8)
            {
                return false;
            }
            int end = (int)Math.Min(declaredSize, (ulong)prefix.Length);
            for (int offset = header; offset <= end - 4; offset += 4)
            {
                if (offset == header + 4)
                {
                    continue;
                }
                ReadOnlySpan<byte> brand = prefix.Slice(offset, 4);
                if (brand.SequenceEqual("heic"u8) || brand.SequenceEqual("heix"u8) || brand.SequenceEqual("hevc"u8)
                    || brand.SequenceEqual("hevx"u8) || brand.SequenceEqual("mif1"u8) || brand.SequenceEqual("msf1"u8))
                {
                    return true;
                }
            }
            return false;
        }

        public async ValueTask<VideoFrame> DecodeAsync(ReadOnlyMemory<byte> encoded, ImageDecodeRequest request,
            MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(allocator);
            request.Validate(Features);
            cancellationToken.ThrowIfCancellationRequested();
            HeicDocument document = HeicDocument.Load(encoded, options);
            RgbaImage image = await document.DecodePrimaryAsync(cancellationToken).ConfigureAwait(false);
            return image.ToVideoFrame(allocator, sampleAspectRatio, cancellationToken);
        }
    }
}
