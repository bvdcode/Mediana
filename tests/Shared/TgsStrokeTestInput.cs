// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;
using Mediana.Images.Tgs;
using System.Numerics;
using System.Text;

namespace Mediana.Tests
{
    internal static class TgsStrokeTestInput
    {
        internal static void VerifyZeroPatterns()
        {
            VectorPath path = new VectorPath().MoveTo(2, 4).LineTo(14, 4).LineTo(14, 12);
            foreach (StrokeCap cap in Enum.GetValues<StrokeCap>())
            {
                StrokeStyle solid = new() { Width = 3, Cap = cap, Join = StrokeJoin.Round };
                VectorPath expected = PathStroker.Outline(path, solid, Matrix3x2.Identity, default);
                foreach (float[] pattern in new float[][] { [0], [0, 0], [0, 0, 0] })
                {
                    foreach (float offset in new[] { -17f, 0f, 100f })
                    {
                        VectorPath actual = PathStroker.Outline(path, solid with { Dashes = pattern, DashOffset = offset }, Matrix3x2.Identity, default);
                        if (!actual.Elements.SequenceEqual(expected.Elements))
                        {
                            throw new InvalidDataException("An all-zero dash pattern must produce the solid outline.");
                        }
                    }
                }
            }
        }

        internal static void VerifyAnimatedZeroDash()
        {
            TgsDocument animated = Read("""
                {"a":1,"k":[
                    {"t":10,"s":[0],"e":[8],"o":{"x":[0.167],"y":[0.333]},"i":{"x":[0.833],"y":[0.667]}},
                    {"t":20,"s":[8]}]}
                """);
            DrawingRenderOptions options = new() { Supersampling = 1 };
            byte[] solid = TgsRenderer.RenderFrame(Read("{\"k\":0}"), 0, options).Pixels.ToArray();
            byte[] dashed = TgsRenderer.RenderFrame(Read("{\"k\":8}"), 0, options).Pixels.ToArray();
            if (solid.SequenceEqual(dashed))
            {
                throw new InvalidDataException("The dash fixture must distinguish solid and dashed strokes.");
            }

            foreach (double frame in new[] { 0d, 5d, 10d, 20d, 25d })
            {
                byte[] expected = frame <= 10 ? solid : dashed;
                using PixelBuffer actual = TgsRenderer.RenderFrame(animated, frame, options);
                if (!actual.Pixels.Span.SequenceEqual(expected))
                {
                    throw new InvalidDataException("Animated stroke dashes must hold exact endpoint values.");
                }
            }
        }

        private static TgsDocument Read(string dash)
        {
            string json = """
                {"v":"5.7","w":32,"h":16,"fr":30,"ip":0,"op":30,"layers":[
                  {"ty":4,"ip":0,"op":30,"shapes":[
                    {"ty":"sh","ks":{"k":{"v":[[2,8],[30,8]],"i":[[0,0],[0,0]],"o":[[0,0],[0,0]],"c":false}}},
                    {"ty":"st","c":{"k":[1,0,0,1]},"o":{"k":100},"w":{"k":3},"lc":1,"lj":1,"d":[{"n":"d","v":
                """ + dash + "}]}]}]}";
            return TgsReader.Read(Encoding.UTF8.GetBytes(json));
        }
    }
}
