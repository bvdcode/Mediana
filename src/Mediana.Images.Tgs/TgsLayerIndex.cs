// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Mediana.Images.Tgs
{
    internal class TgsLayerIndex
    {
        private readonly JsonElement[] _layers;
        private readonly Dictionary<double, int> _positions = new();
        private readonly HashSet<double> _ambiguous = new();

        internal TgsLayerIndex(JsonElement[] layers)
        {
            _layers = layers;
            for (int position = 0; position < layers.Length; position++)
            {
                JsonElement index = TgsJson.Get(layers[position], "ind");
                if (index.ValueKind == JsonValueKind.Number && !_positions.TryAdd(TgsJson.Number(index), position))
                {
                    _ambiguous.Add(TgsJson.Number(index));
                }
            }
        }

        internal int Find(double index)
        {
            if (_ambiguous.Contains(index))
            {
                throw new InvalidDataException("Ambiguous Lottie layer reference.");
            }

            if (!_positions.TryGetValue(index, out int position))
            {
                throw new InvalidDataException("Missing Lottie layer reference.");
            }

            return position;
        }

        internal JsonElement Resolve(double index) => _layers[Find(index)];
    }
}
