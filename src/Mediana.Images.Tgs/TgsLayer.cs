// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Mediana.Drawing;
namespace Mediana.Images.Tgs
{
    internal class TgsLayer
    {
        internal VectorPath? Path { get; init; }
        internal ScenePaint? Fill { get; set; } = new SceneSolid(new Rgba32(0, 0, 0, 255));
        internal ScenePaint? Stroke { get; set; }
        internal StrokeStyle Pen { get; set; } = new() { MiterLimit = 4 };
        internal FillRule Rule { get; set; }
        internal Matrix3x2 Transform { get; set; } = Matrix3x2.Identity;
        internal float Opacity { get; set; } = 1;
        internal float FillOpacity { get; set; } = 1;
        internal float StrokeOpacity { get; set; } = 1;
        internal SceneClip? Clip { get; set; }
        internal SceneMask? Mask { get; set; }
        internal SceneEffect? Filter { get; set; }
        internal ColorBlendMode Blend { get; set; }
        internal List<TgsLayer> Children { get; } = [];
        internal TgsLayer(params TgsLayer[] children) => Children.AddRange(children);
        internal int NodeCount => checked(1 + Children.Sum(child => child.NodeCount));
        internal SceneNode Build(TgsSceneBudget budget, int depth = 0)
        {
            budget.Visit(depth, Path);
            return new SceneNode(Path, Fill, Stroke, Pen, Rule, FillRule.NonZero, Transform, Opacity, FillOpacity, StrokeOpacity, Clip,
                Children.Select(child => child.Build(budget, depth + 1)).ToArray())
            { Mask = Mask, Filter = Filter, BlendMode = Blend };
        }
    }
}
