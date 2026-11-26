// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.IO.Compression;
using System.Numerics;
using Mediana.Codecs;
using Mediana.Codecs.Jpeg;
using Mediana.Codecs.Png;
using Mediana.Codecs.Webp;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Drawing;
using Mediana.Imaging;

namespace Mediana.Images.Apk
{
    internal class ApkDrawableRenderer(ZipArchive archive, AndroidResourceTable resources, RasterCanvas canvas,
        MediaBufferAllocator allocator, CancellationToken token)
    {
        private static readonly ImageDecodeContext RasterDecoders = new([new PngImageDecoder(), new WebpImageDecoder(), new JpegImageDecoder()]);

        public async ValueTask DrawAsync(ResourceValue reference, bool adaptiveLayer)
        {
            token.ThrowIfCancellationRequested();
            ResourceValue value = resources.Resolve(reference);
            if (value.IsColor)
            {
                Rgba32 color = value.Color();
                canvas.FillPath(new VectorPath().Rectangle(0, 0, canvas.Buffer.Width, canvas.Buffer.Height), color,
                    Matrix3x2.Identity, cancellationToken: token);
                return;
            }
            if (value.Text is not string path) { throw new NotSupportedException("Expected an Android drawable file or color."); }
            byte[] bytes = await ApkIconReader.ReadEntryAsync(archive, path, 32 << 20, token).ConfigureAwait(false);
            if (bytes is [3, 0, ..])
            {
                await DrawElementAsync(AndroidElement.Parse(bytes, token), adaptiveLayer).ConfigureAwait(false);
                return;
            }
            ImageDecodeRequest request = new() { MaximumPixels = 16 * 1024 * 1024 };
            using VideoFrame frame = await ImageResources.DecodeAsync(bytes, RasterDecoders, request, allocator, AndroidBitmapColor, token).ConfigureAwait(false);
            float width = frame.Format.Width, height = frame.Format.Height;
            Matrix3x2 transform = Viewport(width, height, adaptiveLayer);
            canvas.DrawImage(frame, transform, cancellationToken: token);
        }

        private async ValueTask DrawElementAsync(AndroidElement element, bool adaptiveLayer)
        {
            switch (element.Name)
            {
                case "adaptive-icon" when !adaptiveLayer:
                    await DrawLayerAsync(element, "background").ConfigureAwait(false);
                    await DrawLayerAsync(element, "foreground").ConfigureAwait(false);
                    break;
                case "vector":
                    AndroidVectorRenderer vector = new(resources, token);
                    (float width, float height) = vector.Viewport(element);
                    vector.Draw(element, canvas, Viewport(width, height, adaptiveLayer));
                    break;
                default:
                    throw new NotSupportedException($"Unsupported Android drawable '{element.Name}'.");
            }
        }

        private async ValueTask DrawLayerAsync(AndroidElement icon, string name)
        {
            AndroidElement? layer = icon.Child(name);
            if (layer is null) { throw new InvalidDataException($"Adaptive icon is missing its {name}."); }
            if (layer.Attributes.TryGetValue("drawable", out ResourceValue drawable))
            {
                await DrawAsync(drawable, true).ConfigureAwait(false);
            }
            else if (layer.Children.Count == 1)
            {
                await DrawElementAsync(layer.Children[0], true).ConfigureAwait(false);
            }
            else { throw new InvalidDataException($"Invalid adaptive icon {name}."); }
        }

        private static VideoColorInfo AndroidBitmapColor(VideoFormat format)
        {
            VideoColorInfo color = format.GetColorInterpretation();
            if (color.Primaries == ColorPrimaries.Unspecified) { color = color with { Primaries = ColorPrimaries.Bt709 }; }
            if (color.Transfer == TransferFunction.Unspecified) { color = color with { Transfer = TransferFunction.Srgb }; }
            return color;
        }

        private Matrix3x2 Viewport(float width, float height, bool adaptiveLayer)
        {
            float size = canvas.Buffer.Width;
            // The launcher shows the center 72 dp of each 108 dp adaptive layer.
            if (adaptiveLayer) { return Matrix3x2.CreateScale(1.5f * size / width, 1.5f * size / height) * Matrix3x2.CreateTranslation(-size / 4, -size / 4); }
            float scale = size / Math.Max(width, height);
            return Matrix3x2.CreateScale(scale) * Matrix3x2.CreateTranslation((size - width * scale) / 2, (size - height * scale) / 2);
        }
    }
}
