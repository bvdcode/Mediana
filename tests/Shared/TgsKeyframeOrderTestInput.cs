// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;
using Mediana.Images.Tgs;
using System.Text;

namespace Mediana.Tests
{
    internal static class TgsKeyframeOrderTestInput
    {
        internal static byte[] Unordered => Input("""
            [{"t":20,"s":[4,0],"h":1},{"t":0,"s":[0,0],"h":1},
             {"t":10,"s":[2,0],"h":1},{"t":10,"s":[6,0],"h":1}]
            """);

        internal static void Verify()
        {
            byte[] input = Unordered;
            byte[] original = input.ToArray();
            TgsDocument restored = TgsReader.Read(input);
            TgsDocument ordered = TgsReader.Read(Input("""
                [{"t":0,"s":[0,0],"h":1},{"t":10,"s":[2,0],"h":1},
                 {"t":10,"s":[6,0],"h":1},{"t":20,"s":[4,0],"h":1}]
                """));
            if (restored.Warnings.Count != 1 || ordered.Warnings.Count != 0 || !input.SequenceEqual(original))
            {
                throw new InvalidDataException("Restoring keyframe order must be diagnosed without changing source bytes.");
            }

            foreach (double frame in new[] { 0d, 5d, 10d, 15d, 20d, 25d })
            {
                TgsFrame actual = TgsRenderer.RenderFrameDetailed(restored, frame, new() { Supersampling = 1 });
                using PixelBuffer pixels = actual.Image;
                using PixelBuffer expected = TgsRenderer.RenderFrame(ordered, frame, new() { Supersampling = 1 });
                if (!pixels.Pixels.Span.SequenceEqual(expected.Pixels.Span) || !actual.Warnings.SequenceEqual(restored.Warnings))
                {
                    throw new InvalidDataException("Keyframes with equal times must retain their original relative order.");
                }
            }
        }

        private static byte[] Input(string keys) => Encoding.UTF8.GetBytes("""
            {"v":"5.7","w":16,"h":8,"fr":30,"ip":0,"op":30,"layers":[
              {"ty":1,"ind":1,"sw":4,"sh":8,"sc":"#ff0000","ks":{"p":{"a":1,"k":
            """ + keys + "}}}]}");
    }
}
