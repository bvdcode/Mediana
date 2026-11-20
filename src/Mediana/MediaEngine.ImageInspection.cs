// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Core.Metadata;

namespace Mediana
{
    public partial class MediaEngine
    {
        /// <summary>Inspects complete encoded image bytes without an input copy or pixel decoding.
        /// Explicit inspectors take precedence over decoder-supplied metadata readers. Metadata owns its values.</summary>
        public ValueTask<ImageInfo> InspectImageAsync(ReadOnlyMemory<byte> encoded, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IImageInfoReader reader = FindImageInfoReader(encoded.Span[..Math.Min(ImageInspectionProbeLength, encoded.Length)], encoded.Length)
                ?? throw new NotSupportedException("No registered image metadata reader recognizes the input.");
            return reader.ReadInfoAsync(encoded, cancellationToken);
        }

        /// <summary>Returns unchanged encoded image previews. Results borrow the input; its owner keeps storage valid and unchanged.</summary>
        public ValueTask<IReadOnlyList<EncodedImagePreview>> ReadImagePreviewsAsync(ReadOnlyMemory<byte> encoded,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IImageInfoReader? reader = FindImageInfoReader(encoded.Span[..Math.Min(ImageInspectionProbeLength, encoded.Length)], encoded.Length);
            return reader is IImagePreviewReader previews ? previews.ReadPreviewsAsync(encoded, cancellationToken)
                : throw new NotSupportedException("The recognized image metadata reader does not expose encoded previews.");
        }

        private IImageInfoReader? FindImageInfoReader(ReadOnlySpan<byte> prefix, long? length)
        {
            IImageInspector? selected = null;
            foreach (IImageInspector reader in imageInspectors)
            {
                if (!reader.CanRead(prefix, length)) { continue; }
                if (selected is not null) { throw new NotSupportedException("Multiple image metadata readers recognize the input."); }
                selected = reader;
            }
            if (selected is not null) { return selected; }
            IImageDecoder? decoder = FindImageDecoder(prefix, length);
            if (decoder is null) { return null; }
            return decoder as IImageInfoReader
                ?? throw new NotSupportedException($"Image decoder '{decoder.ImplementationId}' does not expose image metadata.");
        }
    }
}
