// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;
using System.Collections.Generic;
using System.Linq;

namespace Mediana.Drawing
{
    internal partial record SceneFilter
    {
        private Dictionary<string, int> LastUses()
        {
            var uses = new Dictionary<string, int>(System.StringComparer.Ordinal);
            for (int index = 0; index < Steps.Length; index++)
            {
                foreach (string input in Steps[index].Inputs)
                {
                    uses[input] = index;
                    if (input == "SourceAlpha")
                    {
                        uses["SourceGraphic"] = index;
                    }
                }
            }
            return uses;
        }


        private static void ReleaseResults(Dictionary<string, PixelBuffer> results,
            Dictionary<PixelBuffer, (float X, float Y, float Width, float Height)> regions, PixelBuffer previous,
            Dictionary<string, int> lastUses, int index, SceneBufferScope buffers)
        {
            foreach (string name in results.Keys.Where(name => lastUses.GetValueOrDefault(name, -1) <= index).ToArray())
            {
                results.Remove(name);
            }

            HashSet<PixelBuffer> retained = results.Values.ToHashSet();
            retained.Add(previous);
            foreach (PixelBuffer image in regions.Keys.Where(image => !retained.Contains(image)).ToArray())
            {
                regions.Remove(image);
            }

            buffers.Retain(retained);
        }

        private static bool CanOverwrite(PixelBuffer input, Dictionary<string, PixelBuffer> results, Dictionary<string, int> lastUses, int index, SceneBufferScope buffers)
            => buffers.Owns(input) && !results.Any(pair => ReferenceEquals(pair.Value, input) && lastUses.GetValueOrDefault(pair.Key, -1) > index);

    }
}
