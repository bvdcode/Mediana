// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Codecs.Jpeg;
using Mediana.Codecs.Png;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Drawing;
using Mediana.Processing;
using Mediana.Core.Metadata;
using System.Numerics;

namespace Mediana.Documents.Word
{
    internal class WordPageRasterizer(RasterCanvas canvas, MediaBufferAllocator allocator, WordRenderOptions options,
        CancellationToken token) : IDisposable
    {
        private readonly Dictionary<(string Part, int Reduction), VideoFrame> _images = [];
        private readonly Dictionary<string, ImageInfo> _headers = new(StringComparer.Ordinal);

        internal async ValueTask DrawAsync(IReadOnlyList<WordPaint> paint, Matrix3x2 transform)
        {
            foreach (WordPaint item in paint)
            {
                token.ThrowIfCancellationRequested();
                switch (item)
                {
                    case WordGlyphPaint glyph:
                        Matrix3x2 glyphTransform = Matrix3x2.CreateScale((float)glyph.Scale, (float)-glyph.Scale)
                            * Matrix3x2.CreateTranslation((float)glyph.X, (float)glyph.Baseline) * transform;
                        canvas.FillPath(glyph.Path, glyph.Color, glyphTransform, cancellationToken: token);
                        break;
                    case WordRectanglePaint rectangle:
                        if (rectangle.Width > 0 && rectangle.Height > 0)
                        {
                            VectorPath path = new VectorPath().Rectangle((float)rectangle.X, (float)rectangle.Y, (float)rectangle.Width, (float)rectangle.Height);
                            canvas.FillPath(path, rectangle.Color, transform, cancellationToken: token);
                        }
                        break;
                    case WordImagePaint image:
                        VideoFrame pixels = await ImageAsync(image.Picture, transform);
                        Matrix3x2 imageTransform = Matrix3x2.CreateScale((float)(image.Picture.Width / pixels.Format.Width), (float)(image.Picture.Height / pixels.Format.Height))
                            * Matrix3x2.CreateTranslation((float)image.X, (float)image.Y) * transform;
                        canvas.DrawImage(pixels, imageTransform, cancellationToken: token);
                        break;
                    case WordInlineGraphicPaint graphic:
                        graphic.Graphic.Draw(canvas, Matrix3x2.CreateScale((float)graphic.Size)
                            * Matrix3x2.CreateTranslation((float)graphic.X, (float)graphic.Top) * transform, token);
                        break;
                    case WordGroupPaint group:
                        Matrix3x2 placed = Matrix3x2.CreateTranslation((float)group.X, (float)group.Y) * transform;
                        bool clipped = group.ClipWidth.HasValue && group.ClipHeight.HasValue;
                        if (clipped) { canvas.Save(); }
                        try
                        {
                            if (clipped)
                            {
                                VectorPath clip = new VectorPath().Rectangle(0, 0, (float)group.ClipWidth!.Value, (float)group.ClipHeight!.Value);
                                canvas.ClipPath(clip, placed, cancellationToken: token);
                            }
                            await DrawAsync(group.Children, placed);
                        }
                        finally
                        {
                            if (clipped) { canvas.Restore(); }
                        }
                        break;
                    default: throw new NotSupportedException("Unsupported Word paint command.");
                }
            }
        }

        private async ValueTask<VideoFrame> ImageAsync(WordPicture picture, Matrix3x2 transform)
        {
            PngImageDecoder png = new(new() { MaximumPixels = options.MaximumImagePixels });
            JpegImageDecoder jpeg = new(new() { MaximumPixels = options.MaximumImagePixels });
            IImageDecoder decoder = picture.Encoded.Span switch
            {
                ReadOnlySpan<byte> bytes when png.CanDecode(bytes) => png,
                ReadOnlySpan<byte> bytes when jpeg.CanDecode(bytes) => jpeg,
                _ => throw new NotSupportedException("Word pictures require a supported PNG or JPEG image.")
            };
            int factor = 1;
            if (decoder == png)
            {
                if (!_headers.TryGetValue(picture.PartName, out ImageInfo? info))
                {
                    info = await png.ReadInfoAsync(picture.Encoded, token);
                    _headers.Add(picture.PartName, info);
                }
                double scale = Math.Sqrt(transform.M11 * transform.M11 + transform.M12 * transform.M12);
                double targetWidth = Math.Max(1, picture.Width * scale), targetHeight = Math.Max(1, picture.Height * scale);
                factor = Math.Clamp((int)Math.Floor(Math.Min(info.Width / targetWidth, info.Height / targetHeight)), 1, 256);
                decoder = new PngImageDecoder(new() { MaximumPixels = options.MaximumImagePixels, ReductionFactor = factor });
            }
            if (_images.TryGetValue((picture.PartName, factor), out VideoFrame? existing)) { return existing; }
            using VideoFrame decoded = await decoder.DecodeAsync(picture.Encoded, new() { WarningHandler = options.WarningHandler }, allocator, token);
            VideoColorInfo declared = decoded.Format.GetColorInterpretation();
            VideoColorInfo interpretation = options.ImageColorResolver?.Invoke(decoded.Format) ?? declared;
            if (interpretation != declared) { options.WarningHandler?.Invoke("An embedded Word picture uses the configured interpretation for missing color labels."); }
            VideoFrame frame = SrgbConverter.Apply(decoded, interpretation, allocator, token);
            bool retained = false;
            try
            {
                _images.Add((picture.PartName, factor), frame);
                retained = true;
                return frame;
            }
            finally
            {
                if (!retained) { frame.Dispose(); }
            }
        }

        public void Dispose()
        {
            foreach (VideoFrame frame in _images.Values) { frame.Dispose(); }
            _images.Clear();
        }
    }
}
