// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Threading;

namespace Mediana.Images.Tgs
{
    internal partial class TgsSceneBuilder
    {
        private readonly TgsDocument _document;
        private readonly CancellationToken _token;
        private readonly Dictionary<string, JsonElement> _assets = new(StringComparer.Ordinal);
        private readonly HashSet<string> _activeAssets = new();
        private readonly HashSet<string> _warnings = new();
        private readonly TgsSceneBudget _sceneBudget;
        private int _items;
        private int _commands;
        private int _id;
        internal IEnumerable<string> Warnings => _warnings;
        internal TgsSceneBuilder(TgsDocument document, CancellationToken token)
        {
            _document = document;
            _sceneBudget = new TgsSceneBudget(document.Limits, token);
            _token = token;
            foreach (JsonElement asset in TgsJson.Array(TgsJson.Get(document.Root, "assets")))
            {
                if (!_assets.TryAdd(TgsJson.Text(asset, "id"), asset))
                {
                    throw new InvalidDataException("Duplicate Lottie asset id.");
                }
            }
        }

        internal SceneNode Build(double frame, bool allowPartial)
        {
            TgsLayer root = Layers(TgsJson.Get(_document.Root, "layers"), frame, 0);
            if (!allowPartial && _warnings.Count > 0)
            {
                throw new NotSupportedException(string.Join("; ", _warnings.Order()));
            }

            return root.Build(_sceneBudget);
        }

        private TgsLayer Layers(JsonElement array, double frame, int depth, double width = 0, double height = 0)
        {
            Count(depth);
            JsonElement[] layers = TgsJson.Array(array);
            TgsLayerIndex byIndex = new(layers);

            TgsLayer? group = new TgsLayer();
            var matteLayers = new HashSet<int>();
            for (int i = 0; i < layers.Length; i++)
            {
                if (TgsJson.Number(layers[i], "tt") is >= 1 and <= 4)
                {
                    matteLayers.Add(MatteIndex(layers, i, byIndex));
                }
            }

            for (int i = layers.Length - 1; i >= 0; i--)
            {
                JsonElement layer = layers[i];
                Count(depth);
                if (matteLayers.Contains(i) || TgsJson.Flag(layer, "hd") || frame < TgsJson.Number(layer, "ip", _document.InPoint) || frame >= TgsJson.Number(layer, "op", _document.OutPoint))
                {
                    continue;
                }

                group.Children.Add(PaintLayer(layers, i, byIndex, frame, depth, new HashSet<int>()));
            }

            return group;
        }

        private TgsLayer PaintLayer(JsonElement[] layers, int index, TgsLayerIndex byIndex, double frame, int depth, HashSet<int> active, bool includeHidden = false)
        {
            Count(depth);
            if (!active.Add(index))
            {
                throw new InvalidDataException("Cyclic Lottie track matte.");
            }

            try
            {
                JsonElement layer = layers[index];
                if ((!includeHidden && TgsJson.Flag(layer, "hd")) || frame < TgsJson.Number(layer, "ip", _document.InPoint) || frame >= TgsJson.Number(layer, "op", _document.OutPoint))
                {
                    return new TgsLayer();
                }

                Matrix3x2 matrix = LayerTransform(layer, frame, byIndex, new HashSet<double>());
                TgsLayer content = new TgsLayer(LayerContent(layer, frame, depth + 1));
                Mask(layer, content, frame);
                content = new TgsLayer(content);
                Effects(layer, content, frame);
                content.Transform = matrix;
                content.Opacity = Number(Math.Clamp(TgsValues.Scalar(TgsJson.Get(TgsJson.Get(layer, "ks"), "o"), frame, 100) / 100, 0, 1));
                TgsLayer result = new(content);
                SetMatte(layer, result, layers, byIndex, frame, depth, active);
                int matte = (int)TgsJson.Number(layer, "tt");
                if (matte is >= 1 and <= 4)
                {
                    TgsLayer drawing = PaintLayer(layers, MatteIndex(layers, index, byIndex), byIndex, frame, depth + 1, active);
                    result.Mask = Matte(drawing, matte);
                }

                if (BlendMode((int)TgsJson.Number(layer, "bm")) is ColorBlendMode blend)
                {
                    result.Blend = blend;
                }
                else
                {
                    _warnings.Add("Lottie blend modes");
                }

                return result;
            }
            finally
            {
                active.Remove(index);
            }
        }

        private static int MatteIndex(JsonElement[] layers, int index, TgsLayerIndex byIndex)
        {
            JsonElement target = TgsJson.Get(layers[index], "tp");
            if (target.ValueKind == JsonValueKind.Number)
            {
                return byIndex.Find(TgsJson.Number(target));
            }

            return index > 0 ? index - 1 : throw new InvalidDataException("Missing preceding Lottie track matte.");
        }

        private Matrix3x2 LayerTransform(JsonElement layer, double frame, TgsLayerIndex siblings, HashSet<double> active)
        {
            JsonElement values = TgsJson.Get(layer, "ks");
            Matrix3x2 matrix = Transform(values, frame, TgsJson.Flag(layer, "ao") ? AutoOrientation(TgsJson.Get(values, "p"), frame) : 0);
            JsonElement parent = TgsJson.Get(layer, "parent");
            if (parent.ValueKind == JsonValueKind.Number)
            {
                double index = TgsJson.Number(parent);
                if (!active.Add(index) || active.Count > _document.Limits.MaximumDepth)
                {
                    throw new InvalidDataException("Cyclic/deep Lottie parent chain.");
                }

                matrix *= LayerTransform(siblings.Resolve(index), frame, siblings, active);
            }

            return matrix;
        }

        private TgsLayer LayerContent(JsonElement layer, double frame, int depth)
        {
            int type = (int)TgsJson.Number(layer, "ty", -1);
            if (type == 4)
            {
                return Shapes(TgsJson.Array(TgsJson.Get(layer, "shapes")), frame, depth);
            }

            if (type == 1)
            {
                return new TgsLayer { Path = new VectorPath().Rectangle(0, 0, Number(TgsJson.Number(layer, "sw")), Number(TgsJson.Number(layer, "sh"))), Fill = new SceneSolid(SolidColor(TgsJson.Text(layer, "sc", "#000000"))) };
            }

            if (type == 0)
            {
                return Precomposition(layer, frame, depth);
            }

            return new TgsLayer();
        }

        private TgsLayer Precomposition(JsonElement layer, double frame, int depth)
        {
            string id = TgsJson.Text(layer, "refId");
            if (!_assets.TryGetValue(id, out JsonElement asset))
            {
                throw new InvalidDataException("Missing Lottie precomposition: " + id);
            }

            if (!_activeAssets.Add(id))
            {
                throw new InvalidDataException("Cyclic Lottie precomposition.");
            }

            try
            {
                double stretch = TgsJson.Number(layer, "sr", 1);
                if (stretch == 0)
                {
                    throw new InvalidDataException("Zero Lottie time stretch.");
                }

                JsonElement remap = TgsJson.Get(layer, "tm");
                double time = remap.ValueKind == JsonValueKind.Object ? TgsValues.Scalar(remap, frame) * _document.FrameRate : (frame - TgsJson.Number(layer, "st")) / stretch;
                double width = TgsJson.Number(layer, "w", TgsJson.Number(asset, "w", _document.Width));
                double height = TgsJson.Number(layer, "h", TgsJson.Number(asset, "h", _document.Height));
                TgsLayer group = Layers(TgsJson.Get(asset, "layers"), time, depth, width, height);
                return new TgsLayer(group) { Clip = new SceneClip([new SceneClipShape(new VectorPath().Rectangle(0, 0, Number(width), Number(height)), FillRule.NonZero)], false) };
            }
            finally
            {
                _activeAssets.Remove(id);
            }
        }

        private static double AutoOrientation(JsonElement position, double frame)
        {
            IEnumerable<double> Times(JsonElement property) => TgsJson.Array(TgsJson.Get(property, "k")).Where(k => TgsJson.Get(k, "t").ValueKind == JsonValueKind.Number).Select(k => TgsJson.Number(k, "t"));
            double[] times = (TgsJson.Flag(position, "s") ? Times(TgsJson.Get(position, "x")).Concat(Times(TgsJson.Get(position, "y"))) : Times(position)).ToArray();
            if (times.Length > 0)
            {
                frame = Math.Clamp(frame, times.Min(), times.Max());
            }

            Vector2 delta = TgsValues.Point(position, frame + .01) - TgsValues.Point(position, frame - .01);
            return delta.LengthSquared() < 1e-12 ? 0 : (Math.Atan2(delta.Y, delta.X) * 180) / Math.PI;
        }

        private static Matrix3x2 Transform(JsonElement value, double frame, double orientation = 0) => TgsTransforms.Transform(value, frame, orientation);

        private void Count(int depth)
        {
            _token.ThrowIfCancellationRequested();
            if (++_items > _document.Limits.MaximumItems || depth >= _document.Limits.MaximumDepth)
            {
                throw new InvalidDataException("Lottie expanded scene exceeds limits.");
            }
        }

        private void Geometry(VectorPath path)
        {
            _commands = checked(_commands + path.CommandCount);
            if (_commands > _document.Limits.MaximumPathCommands)
            {
                throw new InvalidDataException("Lottie frame exceeds MaximumPathCommands.");
            }
        }

        private string Id() => "tgs" + (++_id).ToString(CultureInfo.InvariantCulture);
        private static float Number(double value)
        {
            if (!double.IsFinite(value) || Math.Abs(value) > float.MaxValue)
            {
                throw new InvalidDataException("Invalid Lottie coordinate.");
            }

            return (float)value;
        }

        private static TgsLayer PathElement(VectorPath path) => new() { Path = path };

        private static Rgba32 SolidColor(string text)
        {
            if (text.Length == 7 && text[0] == '#' && uint.TryParse(text.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb))
            {
                return new Rgba32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);
            }
            throw new InvalidDataException("Lottie solid color must be #RRGGBB.");
        }
    }
}
