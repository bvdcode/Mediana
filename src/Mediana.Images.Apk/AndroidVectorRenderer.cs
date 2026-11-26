// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using Mediana.Drawing;
using Mediana.Images.Svg;

namespace Mediana.Images.Apk
{
    internal class AndroidVectorRenderer(AndroidResourceTable resources, CancellationToken token)
    {
        public (float Width, float Height) Viewport(AndroidElement vector)
        {
            float width = Number(vector, "viewportWidth"), height = Number(vector, "viewportHeight");
            if (width <= 0 || height <= 0) { throw new InvalidDataException("Invalid Android vector viewport."); }
            return (width, height);
        }

        public void Draw(AndroidElement vector, RasterCanvas canvas, Matrix3x2 transform)
        {
            if (vector.Attributes.ContainsKey("tint")) { throw new NotSupportedException("Tinted Android vector drawables are unsupported."); }
            float alpha = Number(vector, "alpha", 1);
            if (alpha == 1) { DrawChildren(vector, canvas, transform); }
            else { canvas.DrawLayer(layer => DrawChildren(vector, layer, transform), alpha, cancellationToken: token); }
        }

        private void DrawChildren(AndroidElement parent, RasterCanvas canvas, Matrix3x2 transform)
        {
            canvas.Save();
            try
            {
                foreach (AndroidElement child in parent.Children)
                {
                    token.ThrowIfCancellationRequested();
                    switch (child.Name)
                    {
                        case "group":
                            DrawChildren(child, canvas, GroupTransform(child) * transform);
                            break;
                        case "path":
                        case "clip-path":
                            DrawPath(child, canvas, transform);
                            break;
                        default:
                            throw new NotSupportedException($"Unsupported Android vector element '{child.Name}'.");
                    }
                }
            }
            finally { canvas.Restore(); }
        }

        private Matrix3x2 GroupTransform(AndroidElement group)
        {
            Vector2 pivot = new(Number(group, "pivotX"), Number(group, "pivotY"));
            Vector2 translation = new(Number(group, "translateX"), Number(group, "translateY"));
            return Matrix3x2.CreateTranslation(-pivot)
                * Matrix3x2.CreateScale(Number(group, "scaleX", 1), Number(group, "scaleY", 1))
                * Matrix3x2.CreateRotation(Number(group, "rotation") * MathF.PI / 180f)
                * Matrix3x2.CreateTranslation(pivot + translation);
        }

        private void DrawPath(AndroidElement element, RasterCanvas canvas, Matrix3x2 transform)
        {
            ResourceValue data = resources.Resolve(element.Attributes.GetValueOrDefault("pathData"));
            if (data.Text is null) { throw new InvalidDataException("Missing Android vector path data."); }
            VectorPath path = SvgPathParser.Parse(data.Text, cancellationToken: token);
            FillRule rule = Number(element, "fillType") switch
            {
                0 => FillRule.NonZero,
                1 => FillRule.EvenOdd,
                _ => throw ResourceChunk.Invalid()
            };
            if (element.Name == "clip-path") { canvas.ClipPath(path, transform, rule, token); return; }
            float start = Number(element, "trimPathStart"), end = Number(element, "trimPathEnd", 1);
            if (start != 0 || end != 1) { path = path.Trim(start, end, Number(element, "trimPathOffset"), cancellationToken: token); }
            if (element.Attributes.TryGetValue("fillColor", out ResourceValue fill))
            {
                canvas.FillPathAntialiased(path, resources.Resolve(fill).Color(Number(element, "fillAlpha", 1)), transform, rule, cancellationToken: token);
            }
            float width = Number(element, "strokeWidth");
            if (width > 0 && element.Attributes.TryGetValue("strokeColor", out ResourceValue stroke))
            {
                StrokeStyle style = new()
                {
                    Width = width,
                    Cap = Number(element, "strokeLineCap") switch
                    {
                        0 => StrokeCap.Butt, 1 => StrokeCap.Round, 2 => StrokeCap.Square, _ => throw ResourceChunk.Invalid()
                    },
                    Join = Number(element, "strokeLineJoin") switch
                    {
                        0 => StrokeJoin.Miter, 1 => StrokeJoin.Round, 2 => StrokeJoin.Bevel, _ => throw ResourceChunk.Invalid()
                    },
                    MiterLimit = Number(element, "strokeMiterLimit", 4)
                };
                canvas.StrokePathAntialiased(path, resources.Resolve(stroke).Color(Number(element, "strokeAlpha", 1)), style, transform, cancellationToken: token);
            }
        }

        private float Number(AndroidElement element, string name, float defaultValue = 0)
        {
            if (!element.Attributes.TryGetValue(name, out ResourceValue attribute)) { return defaultValue; }
            ResourceValue value = resources.Resolve(attribute);
            float number = value.Type switch
            {
                4 => BitConverter.UInt32BitsToSingle(value.Data),
                0x10 or 0x11 => (int)value.Data,
                _ => throw new NotSupportedException($"Unsupported numeric Android attribute '{name}'.")
            };
            return float.IsFinite(number) ? number : throw ResourceChunk.Invalid();
        }
    }
}
