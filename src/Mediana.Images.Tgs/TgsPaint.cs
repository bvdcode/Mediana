// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;

namespace Mediana.Images.Tgs
{
    internal partial class TgsSceneBuilder
    {
        private void ApplyStyle(TgsLayer path, JsonElement style, double frame)
        {
            string type = TgsJson.Text(style, "ty");
            bool stroke = type is "st" or "gs";
            ScenePaint paint = type is "gf" or "gs" ? Gradient(style, frame) : new SceneSolid(Color(TgsValues.Vector(TgsJson.Get(style, "c"), frame, 0, 0, 0, 1)));
            float opacity = Number(Math.Clamp(TgsValues.Scalar(TgsJson.Get(style, "o"), frame, 100) / 100, 0, 1));
            path.Fill = stroke ? null : paint;
            if (stroke)
            {
                path.Stroke = paint;
                path.StrokeOpacity = opacity;
                var dashes = new List<float>();
                float offset = 0;
                foreach (JsonElement item in TgsJson.Array(TgsJson.Get(style, "d")))
                {
                    double value = TgsValues.Scalar(TgsJson.Get(item, "v"), frame);
                    if (TgsJson.Text(item, "n") == "o") { offset = Number(value); }
                    else { dashes.Add(Number(Math.Max(0, value))); }
                }
                path.Pen = new StrokeStyle
                {
                    Width = Number(Math.Max(0, TgsValues.Scalar(TgsJson.Get(style, "w"), frame, 1))),
                    Cap = TgsJson.Number(style, "lc", 1) switch { 2 => StrokeCap.Round, 3 => StrokeCap.Square, _ => StrokeCap.Butt },
                    Join = TgsJson.Number(style, "lj", 1) switch { 2 => StrokeJoin.Round, 3 => StrokeJoin.Bevel, _ => StrokeJoin.Miter },
                    MiterLimit = Number(Math.Max(1, TgsJson.Number(style, "ml", 4))),
                    Dashes = dashes.ToArray(),
                    DashOffset = offset
                };
            }
            else
            {
                path.FillOpacity = opacity;
                path.Rule = TgsJson.Number(style, "r", 1) == 2 ? FillRule.EvenOdd : FillRule.NonZero;
            }
        }

        private SceneGradient Gradient(JsonElement style, double frame)
        {
            bool radial = TgsJson.Number(style, "t", 1) == 2;
            Vector2 start = TgsValues.Point(TgsJson.Get(style, "s"), frame), end = TgsValues.Point(TgsJson.Get(style, "e"), frame);
            float radius = 0;
            if (radial)
            {
                radius = Vector2.Distance(start, end);
                double highlight = Math.Clamp(TgsValues.Scalar(TgsJson.Get(style, "h"), frame) / 100, -.999, .999);
                double angle = Math.Atan2(end.Y - start.Y, end.X - start.X) + ((TgsValues.Scalar(TgsJson.Get(style, "a"), frame) * Math.PI) / 180);
                end = start;
                start = new Vector2(Number(start.X + (Math.Cos(angle) * radius * highlight)), Number(start.Y + (Math.Sin(angle) * radius * highlight)));
            }
            return new SceneGradient(radial, false, Matrix3x2.Identity, "pad", start, end, radius, 0, GradientStops(style, frame));
        }

        private static (float Offset, Rgba32 Color)[] GradientStops(JsonElement style, double frame)
        {
            JsonElement g = TgsJson.Get(style, "g");
            int count = (int)TgsJson.Number(g, "p");
            double[] values = TgsValues.Vector(TgsJson.Get(g, "k"), frame);
            if (count < 0 || count > values.Length / 4 || (values.Length - (count * 4)) % 2 != 0)
            {
                throw new InvalidDataException("Invalid Lottie gradient stop array.");
            }
            var positions = new SortedSet<double>();
            var duplicates = new HashSet<double>();
            for (int i = 0; i < count; i++)
            {
                if (!positions.Add(Math.Clamp(values[i * 4], 0, 1))) { duplicates.Add(Math.Clamp(values[i * 4], 0, 1)); }
            }
            var alphaPositions = new HashSet<double>();
            for (int i = count * 4; i < values.Length; i += 2)
            {
                positions.Add(Math.Clamp(values[i], 0, 1));
                if (!alphaPositions.Add(Math.Clamp(values[i], 0, 1))) { duplicates.Add(Math.Clamp(values[i], 0, 1)); }
            }
            var stops = new List<(float, Rgba32)>();
            void Stop(double at, double sample)
            {
                Rgba32 color = Color([Interpolate(values, count, sample, 0, 4, 1, 0), Interpolate(values, count, sample, 0, 4, 2, 0), Interpolate(values, count, sample, 0, 4, 3, 0)]);
                float alpha = Number(Math.Clamp(Interpolate(values, count, sample, count * 4, 2, 1, 1), 0, 1));
                stops.Add((Number(at), ScenePixels.Alpha(color, alpha)));
            }
            foreach (double at in positions)
            {
                if (duplicates.Contains(at)) { Stop(at, at - 1e-10); Stop(at, at + 1e-10); }
                else { Stop(at, at); }
            }
            return stops.ToArray();
        }

        private static double Interpolate(double[] values, int count, double at, int start, int stride, int channel, double fallback)
        {
            int n = start == 0 ? count : (values.Length - start) / stride;
            if (n == 0)
            {
                return fallback;
            }

            int next = 0;
            while (next < n && values[start + (next * stride)] <= at)
            {
                next++;
            }

            if (next == 0)
            {
                return values[start + channel];
            }

            if (next == n)
            {
                return values[start + ((n - 1) * stride) + channel];
            }

            int a = start + ((next - 1) * stride), b = start + (next * stride);
            double weight = (at - values[a]) / (values[b] - values[a]);
            return values[a + channel] + ((values[b + channel] - values[a + channel]) * weight);
        }

        private static Rgba32 Color(double[] values)
        {
            if (values.Length < 3) { throw new InvalidDataException("Lottie color needs at least three channels."); }
            byte Channel(int i) => (byte)Math.Round(Math.Clamp(values[i], 0, 1) * 255);
            return new Rgba32(Channel(0), Channel(1), Channel(2), 255);
        }
    }
}
