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
        private static IEnumerable<JsonElement> ExpandEffects(JsonElement array)
        {
            foreach (JsonElement effect in TgsJson.Array(array))
            {
                if (TgsJson.Number(effect, "en", 1) == 0 || TgsEffectInfo.IsParameterGroup(effect) || TgsEffectInfo.IsIdentity(effect))
                {
                    continue;
                }

                if (TgsEffectInfo.IsContainer(effect))
                {
                    foreach (JsonElement nested in ExpandEffects(TgsJson.Get(effect, "ef")))
                    {
                        yield return nested;
                    }
                }
                else
                {
                    yield return effect;
                }
            }
        }

        private void FlushEffect(TgsLayer content, List<SceneFilterStep> filter)
        {
            if (filter.Count == 0) { return; }
            TgsLayer group = new(content.Children.ToArray()) { Filter = Filter(filter) };
            content.Children.Clear();
            content.Children.Add(group);
        }
        private static void LevelsEffect(List<SceneFilterStep> filter, Func<int, double, double> scalar)
        {
            float[] Table(int offset)
            {
                double black = scalar(offset, 0), white = scalar(offset + 1, 1), gamma = Math.Max(.0001, scalar(offset + 2, 1));
                double low = scalar(offset + 3, 0), high = scalar(offset + 4, 1);
                return Enumerable.Range(0, 256).Select(i =>
                {
                    double t = white == black ? (i / 255d >= white ? 1 : 0) : Math.Clamp(((i / 255d) - black) / (white - black), 0, 1);
                    return Number(low + ((high - low) * Math.Pow(t, 1 / gamma)));
                }).ToArray();
            }
            filter.Add(SceneEffects.Transfer(SceneEffects.Table(Table(10)), SceneEffects.Table(Table(17)), SceneEffects.Table(Table(24)), SceneEffects.Table(Table(31))));
            Func<float, float> combined = SceneEffects.Table(Table(3));
            filter.Add(SceneEffects.Transfer(combined, combined, combined, x => x));
        }

        private static void TransformEffect(TgsLayer content, JsonElement effect, double frame)
        {
            JsonElement[] values = TgsJson.Array(TgsJson.Get(effect, "ef"));
            JsonElement Value(int index) => index < values.Length ? TgsJson.Get(values[index], "v") : default;
            double Scalar(int index, double fallback = 0) => TgsValues.Scalar(Value(index), frame, fallback);
            Vector2 anchor = TgsValues.Point(Value(0), frame), position = TgsValues.Point(Value(1), frame);
            float height = (float)Scalar(3, 100) / 100, width = Scalar(2, 1) == 1 ? height : (float)Scalar(4, 100) / 100;
            float skew = (float)((Scalar(5) * Math.PI) / 180), axis = (float)(((Scalar(6) + 90) * Math.PI) / 180);
            Matrix3x2 matrix = Matrix3x2.CreateTranslation(-anchor) * Matrix3x2.CreateScale(width, height) * Matrix3x2.CreateRotation(-axis) * new Matrix3x2(1, 0, -MathF.Tan(skew), 1, 0, 0) * Matrix3x2.CreateRotation(axis) * Matrix3x2.CreateRotation((float)((Scalar(7) * Math.PI) / 180)) * Matrix3x2.CreateTranslation(position);
            TgsLayer group = new(content.Children.ToArray()) { Transform = matrix, Opacity = Number(Math.Clamp(Scalar(8, 100) / 100, 0, 1)) };
            content.Children.Clear();
            content.Children.Add(group);
        }

        private void StrokeEffect(JsonElement layer, TgsLayer content, TgsLayer? maskedSource, JsonElement effect, double frame)
        {
            JsonElement[] values = TgsJson.Array(TgsJson.Get(effect, "ef"));
            double Scalar(int index, double fallback = 0) => index < values.Length ? TgsValues.Scalar(TgsJson.Get(values[index], "v"), frame, fallback) : fallback;
            JsonElement[] masks = TgsJson.Array(TgsJson.Get(layer, "masksProperties"));
            int paint = (int)Scalar(10, 1), selected = (int)Scalar(0, 1) - 1;
            bool all = Scalar(1) == 1;
            TgsLayer strokes = new() { Opacity = Number(Math.Clamp(Scalar(6, 1), 0, 1)) };
            StrokeStyle pen = new() { Width = Number(Math.Max(0, Scalar(4, 2)) * 2), Cap = StrokeCap.Round, Join = StrokeJoin.Round, MiterLimit = 4 };
            double[] ink = values.Length > 3 ? TgsValues.Vector(TgsJson.Get(values[3], "v"), frame, new[] { 1d, 1d, 1d }) : new[]
            {
                1d,
                1d,
                1d
            };
            ScenePaint stroke = new SceneSolid(paint == 3 ? new Rgba32(255, 255, 255, 255) : Color(ink));
            double gap = Math.Max(0, (Scalar(4, 2) * 2 * Scalar(9)) / 100);
            if (gap > 0)
            {
                pen = pen with { Dashes = new[] { 1f, Number(gap) } };
            }

            for (int i = 0; i < masks.Length; i++)
            {
                if (!all && i != selected)
                {
                    continue;
                }

                VectorPath path = Bezier(TgsJson.Get(masks[i], "pt"), frame);
                float start = (float)Math.Clamp(Scalar(7) / 100, 0, 1), end = (float)Math.Clamp(Scalar(8, 100) / 100, 0, 1);
                path = path.Trim(Math.Min(start, end), Math.Max(start, end), cancellationToken: _token);
                Geometry(path);
                strokes.Children.Add(new TgsLayer { Path = path, Fill = null, Stroke = stroke, Pen = pen });
            }

            ApplyStrokeEffect(content, maskedSource, strokes, paint);
        }

        private void ApplyStrokeEffect(TgsLayer content, TgsLayer? maskedSource, TgsLayer strokes, int paint)
        {
            if (paint == 3)
            {
                TgsLayer group = new(content.Children.ToArray()) { Mask = FullMask(strokes, false) };
                content.Children.Clear();
                content.Children.Add(group);
            }
            else if (paint is 1 or 2)
            {
                if (maskedSource is not null) { maskedSource.Mask = null; }
                if (paint == 2)
                {
                    content.Children.Clear();
                }

                content.Children.Add(strokes);
            }
            else
            {
                throw new InvalidDataException("Invalid Lottie Stroke paint style.");
            }
        }
    }
}
