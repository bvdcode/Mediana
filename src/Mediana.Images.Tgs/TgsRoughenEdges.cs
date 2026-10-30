// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;
using System;
using System.Text.Json;
using System.Collections.Generic;
using System.Numerics;

namespace Mediana.Images.Tgs
{
    internal partial class TgsSceneBuilder
    {
        private void RoughenEdges(List<SceneFilterStep> filter, Func<int, double, double> scalar, JsonElement[] values, double frame)
        {
            double border = scalar(2, 0);
            if (border == 0)
            {
                return;
            }

            if (!SupportedRoughenMode(scalar, values, frame))
            {
                _warnings.Add("Roughen Edges requires Roughen mode, zero stretch/offset/evolution and no evolution cycle");
                return;
            }

            double complexity = scalar(8, 2), scale = scalar(5, 100), influence = scalar(4, 1), sharpness = scalar(3, 1);
            ValidateRoughenParameters(border, complexity, scale, influence, sharpness);

            string original = Id(), alpha = Id(), blurred = Id(), noise = Id(), matte = Id();
            double contrast = Math.Max(.25, sharpness) * 4;
            filter.AddRange([
                SceneEffects.Identity() with { Result = original },
                SceneEffects.Matrix([0,0,0,0,0, 0,0,0,0,0, 0,0,0,0,0, 0,0,0,1,0]) with { Input = original, Result = alpha },
                SceneEffects.Blur(new Vector2(Number(border / 2))) with { Input = alpha, Result = blurred },
                SceneEffects.Noise(new Vector2(Number(1 / scale)), (int)complexity, (long)Number(scalar(13, 0))) with { Result = noise },
                SceneEffects.Composite("arithmetic", new Vector4(0, Number(contrast), Number((-contrast * influence) / 2), Number(.5 - (contrast * .75) + ((contrast * influence) / 4)))) with { Input = blurred, SecondInput = noise, Result = matte },
                SceneEffects.Composite("in") with { Input = original, SecondInput = matte }
            ]);
        }

        private static bool SupportedRoughenMode(Func<int, double, double> scalar, JsonElement[] values, double frame)
        {
            double[] offset = values.Length > 7 ? TgsValues.Vector(TgsJson.Get(values[7], "v"), frame, 0, 0) : new[] { 0d, 0d };
            return scalar(0, 1) == 1 && scalar(6, 0) == 0 && scalar(9, 0) == 0 && scalar(11, 0) == 0 && offset.Length >= 2 && offset[0] == 0 && offset[1] == 0;
        }

        private static void ValidateRoughenParameters(double border, double complexity, double scale, double influence, double sharpness)
        {
            if (border < 0 || border > 512 || complexity < 1 || complexity > 12 || complexity != Math.Floor(complexity) || scale <= 0 || influence < 0 || influence > 2 || sharpness < 0 || sharpness > 10)
            {
                throw new System.IO.InvalidDataException("Roughen Edges parameters exceed supported limits.");
            }
        }
    }
}
