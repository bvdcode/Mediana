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
        private SceneRegion FullRegion => new(-_document.Width * 8, -_document.Height * 8, _document.Width * 17, _document.Height * 17, false);
        private SceneFilter Filter(List<SceneFilterStep> steps) => new(FullRegion, false, steps.ToArray(), false);
        private SceneMask FullMask(TgsLayer content, bool luminance) => new(content.Build(), FullRegion, false, luminance, false);
        private void Mask(JsonElement layer, TgsLayer content, double frame)
        {
            JsonElement[] masks = TgsJson.Array(TgsJson.Get(layer, "masksProperties")).Where(m => TgsJson.Text(m, "mode", "a") != "n").ToArray();
            if (masks.Length == 0) { return; }
            var filter = new List<SceneFilterStep>();
            string? current = null;
            for (int i = 0; i < masks.Length; i++)
            {
                Count(0);
                JsonElement mask = masks[i];
                VectorPath path = Bezier(TgsJson.Get(mask, "pt"), frame);
                Geometry(path);
                string next = "mask" + i;
                TgsLayer drawing = PathElement(path);
                drawing.Fill = new SceneSolid(new Rgba32(255, 255, 255, 255));
                drawing.Opacity = Number(Math.Clamp(TgsValues.Scalar(TgsJson.Get(mask, "o"), frame, 100) / 100, 0, 1));
                filter.Add(SceneEffects.Image(drawing.Build()));
                float expansion = Number(TgsValues.Scalar(TgsJson.Get(mask, "x"), frame));
                if (expansion != 0) { filter.Add(SceneEffects.Morphology(Math.Abs(expansion), expansion > 0)); }
                Vector2 feather = TgsValues.Point(TgsJson.Get(mask, "f"), frame);
                if (feather != Vector2.Zero) { filter.Add(SceneEffects.Blur(new Vector2(Math.Max(0, feather.X) / 2, Math.Max(0, feather.Y) / 2))); }
                filter.Add(SceneEffects.Matrix([0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 1]) with { Result = next });
                bool inverse = TgsJson.Flag(mask, "inv");
                string mode = TgsJson.Text(mask, "mode", "a");
                if (inverse ^ (mode == "s")) { filter.Add(InvertRgb(next)); }
                if (current is null) { current = next; continue; }
                ColorBlendMode operation = mode switch
                {
                    "s" or "i" => ColorBlendMode.Multiply,
                    "f" => ColorBlendMode.Difference,
                    "l" => ColorBlendMode.Lighten,
                    "d" => ColorBlendMode.Darken,
                    _ => ColorBlendMode.Screen
                };
                filter.Add(SceneEffects.Blend(operation) with { Input = current, SecondInput = next, Result = next });
                current = next;
            }
            content.Mask = FullMask(new TgsLayer { Filter = Filter(filter) }, true);
        }

        private SceneMask Matte(TgsLayer drawing, int type)
        {
            SceneMask normal = FullMask(drawing, type is 3 or 4);
            if (type is not 2 and not 4) { return normal; }
            TgsLayer rectangle = new() { Path = new VectorPath().Rectangle(0, 0, _document.Width, _document.Height), Fill = new SceneSolid(new Rgba32(255, 255, 255, 255)), Mask = normal };
            TgsLayer inverted = new(rectangle) { Filter = Filter([SceneEffects.Transfer(x => x, x => x, x => x, x => 1 - x)]) };
            return FullMask(inverted, false);
        }
        private static SceneFilterStep InvertRgb(string result) => SceneEffects.Transfer(x => 1 - x, x => 1 - x, x => 1 - x, x => x) with { Result = result };
        private static ColorBlendMode BlendMode(int mode) => mode switch
        {
            0 => ColorBlendMode.Normal,
            1 => ColorBlendMode.Multiply,
            2 => ColorBlendMode.Screen,
            3 => ColorBlendMode.Overlay,
            4 => ColorBlendMode.Darken,
            5 => ColorBlendMode.Lighten,
            6 => ColorBlendMode.ColorDodge,
            7 => ColorBlendMode.ColorBurn,
            8 => ColorBlendMode.HardLight,
            9 => ColorBlendMode.SoftLight,
            10 => ColorBlendMode.Difference,
            11 => ColorBlendMode.Exclusion,
            12 => ColorBlendMode.Hue,
            13 => ColorBlendMode.Saturation,
            14 => ColorBlendMode.Color,
            15 => ColorBlendMode.Luminosity,
            16 => ColorBlendMode.Add,
            17 => ColorBlendMode.HardMix,
            _ => throw new NotSupportedException("Unknown Lottie blend mode: " + mode)
        };
    }
}
