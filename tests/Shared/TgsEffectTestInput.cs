// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;
using Mediana.Images.Tgs;
using System.Text;

namespace Mediana.Tests
{
    internal static class TgsEffectTestInput
    {
        internal static void VerifyParameterGroups()
        {
            TgsDocument document = Read("""
                [{"ty":5,"nm":"Layer selector","ef":[{"ty":10,"v":{"a":0,"k":8}}]},
                 {"ty":5,"ef":[{"ty":5,"ef":[{"ty":0,"v":{"a":0,"k":15}}]}]}]
                """);
            if (document.UnsupportedFeatures.Count != 0)
            {
                throw new InvalidDataException("Effect parameter groups must not be classified as visual effects.");
            }

            using PixelBuffer actual = TgsRenderer.RenderFrame(document, 0, new() { Supersampling = 1 });
            using PixelBuffer expected = TgsRenderer.RenderFrame(Read("[]"), 0, new() { Supersampling = 1 });
            if (!actual.Pixels.Span.SequenceEqual(expected.Pixels.Span))
            {
                throw new InvalidDataException("Parameter-only groups must not change layer pixels.");
            }

            TgsDocument unknown = Read("""
                [{"ty":5,"mn":"Unsupported Visual Effect","ef":[{"ty":10,"v":{"k":8}}]}]
                """);
            if (unknown.UnsupportedFeatures.Count == 0)
            {
                throw new InvalidDataException("A named visual effect must not be mistaken for a parameter group.");
            }
        }

        internal static void VerifyInvert()
        {
            TgsDocument document = Read("""
                [{"ty":5,"mn":"ADBE Invert","ef":[
                  {"ty":7,"v":{"k":1}},
                  {"ty":0,"v":{"a":1,"k":[
                    {"t":0,"s":[0],"e":[100],"o":{"x":[0.333],"y":[0.333]},"i":{"x":[0.667],"y":[0.667]}},
                    {"t":20,"s":[100]}]}}]}]
                """, 50);
            Rgba32[] expected = [new(127, 191, 223, 128), new(128, 128, 128, 128), new(128, 64, 32, 128)];
            for (int i = 0; i < expected.Length; i++)
            {
                using PixelBuffer image = TgsRenderer.RenderFrame(document, i * 10, new() { Supersampling = 1 });
                Rgba32 actual = image[4, 4];
                Rgba32 reference = expected[i];
                if (Math.Abs(actual.R - reference.R) > 1 || Math.Abs(actual.G - reference.G) > 1 || Math.Abs(actual.B - reference.B) > 1 || actual.A != reference.A)
                {
                    throw new InvalidDataException("RGB inversion must blend with original colors and preserve alpha.");
                }
            }
        }

        private static TgsDocument Read(string effects, int opacity = 100)
        {
            string json = """
                {"v":"5.7","w":8,"h":8,"fr":30,"ip":0,"op":30,"layers":[
                  {"ty":1,"ind":1,"ip":0,"op":30,"sw":8,"sh":8,"sc":"#804020","ef":
                """ + effects + ",\"ks\":{\"o\":{\"k\":" + opacity.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}}}]}";
            return TgsReader.Read(Encoding.UTF8.GetBytes(json));
        }
    }
}
