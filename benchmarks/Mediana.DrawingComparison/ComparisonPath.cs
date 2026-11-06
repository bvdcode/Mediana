// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.DrawingComparison
{
    internal class ComparisonPath
    {
        internal List<PathCommand> Commands { get; } = [];
        internal ComparisonPath Move(float x, float y) { Commands.Add(new(PathVerb.Move, new(x, y))); return this; }
        internal ComparisonPath Line(float x, float y) { Commands.Add(new(PathVerb.Line, new(x, y))); return this; }
        internal ComparisonPath Quadratic(Vector2 control, Vector2 end) { Commands.Add(new(PathVerb.Quadratic, control, end)); return this; }
        internal ComparisonPath Cubic(Vector2 first, Vector2 second, Vector2 end) { Commands.Add(new(PathVerb.Cubic, first, second, end)); return this; }
        internal ComparisonPath Close() { Commands.Add(new(PathVerb.Close)); return this; }
        internal ComparisonPath Rectangle(float x, float y, float width, float height) => Move(x, y).Line(x + width, y).Line(x + width, y + height).Line(x, y + height).Close();

        internal static ComparisonPath Capsule(float x, float y, float width, float height)
        {
            const float circle = .55228475f;
            float radius = Math.Min(width, height) / 2, control = radius * circle;
            return new ComparisonPath().Move(x + radius, y).Line(x + width - radius, y)
                .Cubic(new(x + width - radius + control, y), new(x + width, y + radius - control), new(x + width, y + radius))
                .Line(x + width, y + height - radius)
                .Cubic(new(x + width, y + height - radius + control), new(x + width - radius + control, y + height), new(x + width - radius, y + height))
                .Line(x + radius, y + height)
                .Cubic(new(x + radius - control, y + height), new(x, y + height - radius + control), new(x, y + height - radius))
                .Line(x, y + radius)
                .Cubic(new(x, y + radius - control), new(x + radius - control, y), new(x + radius, y)).Close();
        }
    }
}
