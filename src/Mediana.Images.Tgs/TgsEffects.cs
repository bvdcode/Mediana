// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;

namespace Mediana.Images.Tgs
{
    internal partial class TgsSceneBuilder
    {
        private void Effects(JsonElement layer, TgsLayer content, double frame)
        {
            TgsLayer? maskedSource = content.Children.FirstOrDefault();
            List<SceneFilterStep> filter = new List<SceneFilterStep>();
            foreach (JsonElement effect in ExpandEffects(TgsJson.Get(layer, "ef")))
            {
                int type = TgsEffectInfo.Type(effect);
                if (type == 28)
                {
                    continue;
                }

                if (type is 22 or 35)
                {
                    FlushEffect(content, filter);
                    filter = new List<SceneFilterStep>();
                    if (type == 22)
                    {
                        StrokeEffect(layer, content, maskedSource, effect, frame);
                    }
                    else
                    {
                        TransformEffect(content, effect, frame);
                    }
                }
                else
                {
                    Effect(effect, filter, frame);
                }
            }

            FlushEffect(content, filter);
        }

        private void Effect(JsonElement effect, List<SceneFilterStep> filter, double frame)
        {
            if (TgsJson.Number(effect, "en", 1) == 0)
            {
                return;
            }

            int type = TgsEffectInfo.Type(effect);
            JsonElement[] values = TgsJson.Array(TgsJson.Get(effect, "ef"));
            double Scalar(int index, double fallback = 0) => index < values.Length ? TgsValues.Scalar(TgsJson.Get(values[index], "v"), frame, fallback) : fallback;
            double[] Color(int index, double fallback = 0) => index < values.Length ? TgsValues.Vector(TgsJson.Get(values[index], "v"), frame, new[] { fallback, fallback, fallback }) : new[]
            {
                fallback,
                fallback,
                fallback
            };
            if (TgsEffectInfo.IsRoughenEdges(effect))
            {
                RoughenEdges(filter, Scalar, values, frame);
                return;
            }

            if (TgsEffectInfo.IsInvert(effect))
            {
                if (Scalar(0, 1) != 1)
                {
                    _warnings.Add("Lottie Invert channel other than RGB");
                    return;
                }

                float amount = Number(1 - Math.Clamp(Scalar(1) / 100, 0, 1));
                float scale = 1 - (2 * amount);
                filter.Add(SceneEffects.Matrix([scale, 0, 0, 0, amount, 0, scale, 0, 0, amount, 0, 0, scale, 0, amount, 0, 0, 0, 1, 0]));
                return;
            }

            if (type == 24)
            {
                LevelsEffect(filter, Scalar);
                return;
            }

            if (type is 20 or 21 or 23)
            {
                ColorEffect(filter, type, Color, Scalar);
                return;
            }

            if (type == 25)
            {
                double[] color = Color(0);
                double angle = ((Scalar(2) - 90) * Math.PI) / 180, distance = Scalar(3);
                filter.Add(SceneEffects.Shadow(new Vector2(Number(Math.Max(0, Scalar(4)) / 4)), new Vector2(Number(Math.Cos(angle) * distance), Number(Math.Sin(angle) * distance)), TgsSceneBuilder.Color(color), Number(Math.Clamp(Scalar(1, 255) / 255, 0, 1))));
                return;
            }

            if (type == 29)
            {
                double blur = Math.Max(0, Scalar(0)) * .3;
                int dimensions = (int)Scalar(1, 1);
                filter.Add(SceneEffects.Blur(new Vector2(Number(dimensions == 3 ? 0 : blur), Number(dimensions == 2 ? 0 : blur)), Scalar(2) == 1 ? "duplicate" : "none"));
                return;
            }

            _warnings.Add("Lottie effect type " + type);
        }

        private static void ColorEffect(List<SceneFilterStep> filter, int type, Func<int, double, double[]> color, Func<int, double, double> scalar)
        {
            if (type == 21)
            {
                double[] ink = color(2, 0);
                float opacity = Number(Math.Clamp(scalar(6, 1), 0, 1));
                filter.Add(SceneEffects.Matrix([0, 0, 0, 0, Number(ink[0]), 0, 0, 0, 0, Number(ink[1]), 0, 0, 0, 0, Number(ink[2]), 0, 0, 0, opacity, 0]));
                return;
            }
            double[] low = color(type == 20 ? 0 : 2, 0), high = color(type == 20 ? 1 : 0, 1);
            double[] middle = type == 23 ? color(1, .5) : [(low[0] + high[0]) / 2, (low[1] + high[1]) / 2, (low[2] + high[2]) / 2];
            string original = "original" + filter.Count;
            filter.Add(SceneEffects.Identity() with { Result = original });
            float[] gray = new float[20];
            foreach (int row in new[] { 0, 5, 10 })
            {
                for (int column = 0; column < 3; column++) { gray[row + column] = 1f / 3; }
            }
            gray[18] = 1;
            filter.Add(SceneEffects.Matrix(gray) with { Linear = true });
            Func<float, float> Channel(int i) => SceneEffects.Table([Number(low[i]), Number(middle[i]), Number(high[i])]);
            filter.Add(SceneEffects.Transfer(Channel(0), Channel(1), Channel(2), x => x));
            if (type == 20)
            {
                double amount = Math.Clamp(scalar(2, 100) / 100, 0, 1);
                filter.Add(SceneEffects.Composite("arithmetic", new Vector4(0, Number(amount), Number(1 - amount), 0)) with { SecondInput = original });
            }
        }
    }
}
