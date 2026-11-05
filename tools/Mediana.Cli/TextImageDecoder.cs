// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Core.Memory;
using Mediana.Core.Metadata;
using Mediana.Core.Video;
using Mediana.Drawing;

namespace Mediana.Cli
{
    internal class TextImageDecoder(IImageDecoder metadataDecoder, Func<TextOptions, IImageDecoder> createDecoder) : IImageDecoder, IImageInfoReader
    {
        public string ImplementationId => metadataDecoder.ImplementationId;
        public int ProbeLength => metadataDecoder.ProbeLength;
        public ImageDecoderFeatures Features => metadataDecoder.Features;
        public bool CanDecode(ReadOnlySpan<byte> prefix, long? encodedLength = null) => metadataDecoder.CanDecode(prefix, encodedLength);
        public ValueTask<ImageInfo> ReadInfoAsync(ReadOnlyMemory<byte> encoded, CancellationToken cancellationToken = default) =>
            ((IImageInfoReader)metadataDecoder).ReadInfoAsync(encoded, cancellationToken);

        public async ValueTask<VideoFrame> DecodeAsync(ReadOnlyMemory<byte> encoded, ImageDecodeRequest request, MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            TextOptions text = await CommandTextResources.GetAsync(cancellationToken);
            return await createDecoder(text).DecodeAsync(encoded, request, allocator, cancellationToken);
        }
    }
}
