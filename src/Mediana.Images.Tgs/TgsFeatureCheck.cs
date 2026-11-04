// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace Mediana.Images.Tgs
{
    internal class TgsFeatureCheck
    {
        private static readonly HashSet<string> ShapeTypes = new("gr tr sh rc el sr fl st gf gs tm rp mm rd op pb zz no".Split(' '), StringComparer.Ordinal);
        private readonly TgsReaderOptions _limits;
        private readonly CancellationToken _token;
        private readonly HashSet<string> _warnings = new();
        private readonly HashSet<string> _diagnostics = new(StringComparer.Ordinal);
        internal string[] Diagnostics => _diagnostics.Concat(RequiresKeyframeOrdering ? ["Lottie keyframes reordered by time."] : Array.Empty<string>()).Order().ToArray();
        private int _items;
        internal bool RequiresKeyframeOrdering { get; private set; }
        internal TgsFeatureCheck(TgsReaderOptions limits, CancellationToken token)
        {
            _limits = limits;
            _token = token;
        }

        internal string[] Check(JsonElement root)
        {
            Visit(root);
            return _warnings.Order().ToArray();
        }

        private void Visit(JsonElement e, bool layer = false, bool visible = true, bool validate = true, bool effect = false)
        {
            _token.ThrowIfCancellationRequested();
            if (++_items > _limits.MaximumItems)
            {
                throw new InvalidDataException("Lottie exceeds MaximumItems.");
            }

            switch (e.ValueKind)
            {
                case JsonValueKind.Object:
                    if (effect && TgsEffectInfo.IsIgnored(e, _limits))
                    {
                        if (visible && TgsJson.Number(e, "en", 1) != 0 && !TgsEffectInfo.IsIdentity(e))
                        {
                            _diagnostics.Add("Lottie Puppet deformation was omitted.");
                        }
                        validate = false;
                    }
                    if (validate)
                    {
                        CheckExpression(e);
                    }
                    visible = visible && !(layer && TgsJson.Flag(e, "hd")) && !TgsEffectInfo.IsParameterGroup(e);
                    if (visible && validate)
                    {
                        Features(e, layer);
                    }

                    foreach (JsonProperty property in e.EnumerateObject())
                    {
                        Visit(property.Value, property.Name == "layers", visible, validate, property.Name == "ef");
                    }

                    break;
                case JsonValueKind.Array:
                    if (validate)
                    {
                        ValidateKeys(e);
                    }
                    foreach (JsonElement child in e.EnumerateArray())
                    {
                        Visit(child, layer, visible, validate, effect);
                    }

                    break;
                case JsonValueKind.Number:
                    TgsJson.Number(e);
                    break;
            }
        }

        private void CheckExpression(JsonElement property)
        {
            if (TgsJson.Get(property, "x").ValueKind != JsonValueKind.String)
            {
                return;
            }
            if (!_limits.IgnoreExpressions)
            {
                throw new NotSupportedException("Lottie expressions are not supported.");
            }
            if (TgsJson.Get(property, "k").ValueKind is not (JsonValueKind.Number or JsonValueKind.Array or JsonValueKind.Object))
            {
                throw new InvalidDataException("A Lottie expression property must have a stored value or keyframes.");
            }
            _diagnostics.Add("Lottie expressions were not evaluated; stored property values and keyframes were used.");
        }

        private void ValidateKeys(JsonElement array)
        {
            if (array.GetArrayLength() == 0 || TgsJson.Get(array[0], "ty").ValueKind != JsonValueKind.Undefined || TgsJson.Get(array[0], "t").ValueKind != JsonValueKind.Number)
            {
                return;
            }

            double previous = double.NegativeInfinity;
            foreach (JsonElement key in array.EnumerateArray())
            {
                double next = TgsJson.Number(key, "t", double.NaN);
                if (!double.IsFinite(next))
                {
                    throw new InvalidDataException("Each Lottie keyframe must have a finite time.");
                }

                RequiresKeyframeOrdering |= next < previous;

                previous = next;
            }
        }

        private void Features(JsonElement e, bool layer)
        {
            JsonElement type = TgsJson.Get(e, "ty");
            if (type.ValueKind == JsonValueKind.String)
            {
                ShapeFeatures(e, type.GetString()!);
            }
            else if (layer)
            {
                LayerType(type);
            }

            if (TgsJson.Flag(e, "ddd"))
            {
                _warnings.Add("Lottie 3D layers");
            }

            if (TgsJson.Number(e, "bm") is < 0 or > 17)
            {
                _warnings.Add("Lottie blend modes");
            }

            if (TgsJson.Number(e, "tt") is < 0 or > 4)
            {
                _warnings.Add("Lottie track mattes");
            }

            PropertyFeatures(e, layer);
        }

        private void LayerType(JsonElement type)
        {
            if (type.ValueKind == JsonValueKind.Number && TgsJson.Number(type) is not 0 and not 1 and not 3 and not 4)
            {
                _warnings.Add("Lottie layer type " + type.GetRawText());
            }
        }

        private void PropertyFeatures(JsonElement e, bool layer)
        {

            if (layer)
            {
                CheckEffects(TgsJson.Get(e, "ef"));
            }

            if (TgsJson.Get(e, "sid").ValueKind == JsonValueKind.String)
            {
                _warnings.Add("Lottie property slots");
            }

            if (TgsJson.Get(e, "masksProperties").ValueKind == JsonValueKind.Array)
            {
                MaskFeatures(TgsJson.Get(e, "masksProperties"));
            }
        }

        private void CheckEffects(JsonElement array)
        {
            foreach (JsonElement effect in TgsJson.Array(array))
            {
                if (TgsJson.Number(effect, "en", 1) == 0)
                {
                    continue;
                }

                if (TgsEffectInfo.IsIgnored(effect, _limits) || TgsEffectInfo.IsParameterGroup(effect) || TgsEffectInfo.IsIdentity(effect) || TgsEffectInfo.IsRoughenEdges(effect) || TgsEffectInfo.IsInvert(effect))
                {
                    continue;
                }

                int type = TgsEffectInfo.Type(effect);
                if (TgsEffectInfo.IsContainer(effect))
                {
                    CheckEffects(TgsJson.Get(effect, "ef"));
                }
                else if (!new[]
                {
                    20,
                    21,
                    22,
                    23,
                    24,
                    25,
                    28,
                    29,
                    35
                }.Contains(type))
                {
                    _warnings.Add("Lottie effect type " + type);
                }
            }
        }

        private void ShapeFeatures(JsonElement e, string type)
        {
            if (!ShapeTypes.Contains(type))
            {
                _warnings.Add("Lottie shape/modifier: " + type);
            }
        }

        private void MaskFeatures(JsonElement masks)
        {
            foreach (JsonElement mask in masks.EnumerateArray())
            {
                if (!new[]
                {
                    "a",
                    "s",
                    "i",
                    "n",
                    "f",
                    "l",
                    "d"
                }.Contains(TgsJson.Text(mask, "mode", "a")))
                {
                    _warnings.Add("Lottie mask boolean operations");
                }
            }
        }
    }
}
