// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.Collections.Generic;
using System.IO;
using Mediana.Images.Heif.Hevc;

namespace Mediana.Images.Heif.Container
{
    /// <summary>
    /// Decodes image items with immutable ancestor paths and read-only document metadata.
    /// </summary>
    internal class ImageItemDecoder(HeifContainer container, IReadOnlyDictionary<uint, ImageProperties> metadata, HeicDecodeOptions options, CancellationToken cancellationToken)
    {
        public async Task<RgbaImage> DecodeAsync(uint id)
        {
            using PixelBufferScope buffers = new PixelBufferScope(false);
            return await DecodeAsync(id, false, null, ReadOnlyMemory<byte>.Empty, null, null, true, buffers);
        }

        private async Task<RgbaImage> DecodeAsync(uint id, bool alpha, ColorEncoding? inheritedColor, ReadOnlyMemory<byte> inheritedIcc,
            AlphaChannelInformation? inheritedAlpha, ImageDecodePath? ancestors, bool parallel, PixelBufferScope buffers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ImageDecodePath path = new ImageDecodePath(id, ancestors, options.MaxDerivationDepth);
            HeifItem item = container.Items[id];
            if (!metadata.TryGetValue(id, out ImageProperties? properties))
            {
                throw new UnsupportedHeicFeatureException("image item type " + item.Type.ToString("X8"));
            }

            ColorEncoding? color = properties.Color ?? inheritedColor;
            ReadOnlyMemory<byte> icc = properties.IccProfile.IsEmpty ? inheritedIcc : properties.IccProfile;
            AlphaChannelInformation? alphaInformation = properties.AlphaInformation ?? inheritedAlpha;
            int expectedWidth = properties.Width;
            int expectedHeight = properties.Height;
            int transformedProperties = 0;
            RgbaImage result;
            if (item.Type == HeifBox.Code("hvc1"))
            {
                ImageCrop crop = ImageCrop.ReadPrefix(expectedWidth, expectedHeight, properties.Transforms);
                expectedWidth = crop.Width;
                expectedHeight = crop.Height;
                transformedProperties = crop.PropertyCount;
                result = await DecodeCodedAsync(item, properties, crop, color, icc, alpha, parallel, buffers, alphaInformation);
            }
            else if (item.Type == HeifBox.Code("grid"))
            {
                ImageOrientation orientation = ImageOrientation.ReadPrefix(properties.Width, properties.Height, properties.Transforms);
                expectedWidth = orientation.Width;
                expectedHeight = orientation.Height;
                transformedProperties = orientation.PropertyCount;
                result = await GridImageDecoder.DecodeAsync(container, item, properties,
                    (target, tileBuffers) => DecodeAsync(target, alpha, color, icc, alphaInformation, path, false, tileBuffers), parallel, buffers, orientation, cancellationToken);
            }
            else if (item.Type == HeifBox.Code("iovl"))
            {
                result = await OverlayImageDecoder.DecodeAsync(container, item, properties,
                    (target, inputBuffers) => DecodeAsync(target, alpha, color, icc, alphaInformation, path, parallel, inputBuffers), buffers, cancellationToken);
            }
            else if (item.Type == HeifBox.Code("iden"))
            {
                result = await DecodeAsync(IdentityInput(item), alpha, color, icc, alphaInformation, path, parallel, buffers);
            }
            else
            {
                throw new UnsupportedHeicFeatureException("image item type " + item.Type.ToString("X8"));
            }

            result = ApplyTransforms(result, expectedWidth, expectedHeight, properties.Transforms, transformedProperties, buffers);

            if (!alpha)
            {
                result = await ApplyAlphaAsync(item, result, path, parallel, buffers);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }

        private async Task<RgbaImage> DecodeCodedAsync(HeifItem item, ImageProperties properties, ImageCrop crop, ColorEncoding? color,
            ReadOnlyMemory<byte> icc, bool alpha, bool parallel, PixelBufferScope buffers, AlphaChannelInformation? alphaInformation)
        {
            DecoderConfiguration configuration = properties.Configuration ?? throw new InvalidDataException("HEVC image configuration is missing.");
            using DecodedPicture picture = await HevcImageDecoder.DecodeAsync(configuration, container.ReadData(item.Id), options,
                parallel ? Environment.ProcessorCount : 1, cancellationToken);
            if (properties.PixelDepthsEssential)
            {
                ValidateDepths(properties.PixelDepths.Span, picture.Sequence);
            }
            if ((picture.Sequence.VisibleWidth != properties.Width) || (picture.Sequence.VisibleHeight != properties.Height))
            {
                throw new InvalidDataException("Decoded image disagrees with its spatial extents.");
            }
            return PictureRenderer.Render(picture, crop, color ?? ColorEncoding.FromVideo(picture.Sequence.Video), icc, alpha, buffers, alphaInformation);
        }

        private async Task<RgbaImage> ApplyAlphaAsync(HeifItem item, RgbaImage color, ImageDecodePath path, bool parallel, PixelBufferScope buffers)
        {
            if (item.References.ContainsKey(HeifBox.Code("prem")))
            {
                throw new UnsupportedHeicFeatureException("premultiplied alpha images");
            }

            uint? alphaId = null;
            foreach (HeifItem candidate in container.Items.Values)
            {
                if (!candidate.References.TryGetValue(HeifBox.Code("auxl"), out IReadOnlyList<uint>? masters) ||
                    !metadata.TryGetValue(candidate.Id, out ImageProperties? auxiliary) || !ImageProperties.IsAlpha(auxiliary.AuxiliaryType))
                {
                    continue;
                }

                foreach (uint master in masters)
                {
                    if (master != item.Id)
                    {
                        continue;
                    }

                    if (alphaId.HasValue)
                    {
                        throw new UnsupportedHeicFeatureException("multiple alpha auxiliary images");
                    }

                    alphaId = candidate.Id;
                }
            }

            if (!alphaId.HasValue)
            {
                return color;
            }

            using PixelBufferScope alphaBuffers = new PixelBufferScope(true);
            RgbaImage alpha = await DecodeAsync(alphaId.Value, true, null, ReadOnlyMemory<byte>.Empty, null, path, parallel, alphaBuffers);
            if ((alpha.Width != color.Width) || (alpha.Height != color.Height))
            {
                throw new InvalidDataException("Alpha image dimensions disagree with the master image.");
            }

            Memory<byte> pixels = buffers.Allocate(color.Pixels.Length);
            color.Pixels.CopyTo(pixels);
            for (int offset = 3; offset < pixels.Length; offset += 4)
            {
                pixels.Span[offset] = alpha.Pixels.Span[offset - 3];
            }

            return new RgbaImage(color.Width, color.Height, pixels, color.ColorInformation);
        }

        private static void ValidateDepths(ReadOnlySpan<byte> depths, SequenceParameters sequence)
        {
            if (depths.IsEmpty)
            {
                return;
            }

            int components = sequence.ChromaFormat == 0 ? 1 : 3;
            if (depths.Length != components)
            {
                throw new InvalidDataException("Pixel information component count disagrees with the HEVC sequence.");
            }

            foreach (byte depth in depths)
            {
                if (depth != sequence.BitDepth)
                {
                    throw new InvalidDataException("Pixel information depth disagrees with the HEVC sequence.");
                }
            }
        }

        private static uint IdentityInput(HeifItem item)
        {
            if (!item.References.TryGetValue(HeifBox.Code("dimg"), out IReadOnlyList<uint>? inputs) || (inputs.Count != 1))
            {
                throw new InvalidDataException("Identity image must reference exactly one input.");
            }
            return inputs[0];
        }

        private static RgbaImage ApplyTransforms(RgbaImage result, int expectedWidth, int expectedHeight, IReadOnlyList<HeifBox> transforms,
            int transformedProperties, PixelBufferScope buffers)
        {
            if ((result.Width != expectedWidth) || (result.Height != expectedHeight))
            {
                throw new InvalidDataException("Decoded image disagrees with its spatial extents.");
            }

            for (int index = transformedProperties; index < transforms.Count; index++)
            {
                result = ImageTransforms.Apply(result, transforms[index], buffers);
            }
            return result;
        }
    }
}
