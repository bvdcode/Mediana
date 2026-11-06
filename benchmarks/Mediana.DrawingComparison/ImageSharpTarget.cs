// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using Mediana.Drawing;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SharpPixel = SixLabors.ImageSharp.PixelFormats.Rgba32;
using TextOptions = SixLabors.Fonts.TextOptions;
using Rgba32 = Mediana.Drawing.Rgba32;

namespace Mediana.DrawingComparison
{
    internal class ImageSharpTarget(DrawingCanvas context, FontFamily family, FontStyle style) : IDrawingTarget
    {
        private readonly Dictionary<float, TextOptions> measurementOptions = [];
        public void PushClip(ComparisonPath path) { context.Save(new() { TextContrast = 0 }); context.Clip(Convert(path)); }
        public void PopClip() => context.Restore();
        public void Clear(Rgba32 color) => context.Clear(new SolidBrush(ToColor(color)));
        public void Fill(ComparisonPath path, Rgba32 color, FillRule rule)
        {
            DrawingOptions options = new()
            {
                TextContrast = 0,
                IntersectionRule = rule switch
                {
                    FillRule.NonZero => IntersectionRule.NonZero,
                    FillRule.EvenOdd => IntersectionRule.EvenOdd,
                    _ => throw new ArgumentOutOfRangeException(nameof(rule))
                }
            };
            context.Save(options);
            context.Fill(new SolidBrush(ToColor(color)), Convert(path));
            context.Restore();
        }
        public void Stroke(ComparisonPath path, Rgba32 color, float width) =>
            context.Draw(new SolidPen(new PenOptions(ToColor(color), width) { StrokeOptions = new() { LineCap = LineCap.Butt, LineJoin = LineJoin.Miter, MiterLimit = 10 } }), Convert(path));
        public void Text(ReadOnlySpan<char> text, float x, float baseline, float size, Rgba32 color)
        {
            RichTextOptions textOptions = new(family.CreateFont(size, style))
            {
                Origin = new(x, baseline),
                TextBaseline = TextBaseline.Alphabetic,
                HintingMode = HintingMode.None,
                Dpi = 72,
                KerningMode = KerningMode.Standard
            };
            context.DrawText(textOptions, text, new SolidBrush(ToColor(color)), null);
        }
        public float Measure(ReadOnlySpan<char> text, float size)
        {
            if (!measurementOptions.TryGetValue(size, out TextOptions? options))
            {
                options = new(family.CreateFont(size, style)) { HintingMode = HintingMode.None, Dpi = 72, KerningMode = KerningMode.Standard };
                measurementOptions.Add(size, options);
            }
            return TextMeasurer.MeasureAdvance(text, options).Width;
        }

        private static Color ToColor(Rgba32 color) => Color.FromPixel(new SharpPixel(color.R, color.G, color.B, color.A));

        private static IPath Convert(ComparisonPath path)
        {
            PathBuilder result = new();
            foreach (PathCommand command in path.Commands)
            {
                Vector2 a = command.First, b = command.Second, c = command.Third;
                switch (command.Verb)
                {
                    case PathVerb.Move: result.MoveTo(a.X, a.Y); break;
                    case PathVerb.Line: result.LineTo(a.X, a.Y); break;
                    case PathVerb.Quadratic: result.QuadraticBezierTo(a, b); break;
                    case PathVerb.Cubic: result.CubicBezierTo(a, b, c); break;
                    case PathVerb.Close: result.CloseFigure(); break;
                    default: throw new ArgumentOutOfRangeException(nameof(path));
                }
            }
            return result.Build();
        }
    }
}
