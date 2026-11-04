// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;
using Mediana.Images.Tgs;
using System.Text;

namespace Mediana.Tests
{
    internal static class TgsStoredValuesTestInput
    {
        internal const string PuppetWarning = "Lottie Puppet deformation was omitted.";
        internal const string ExpressionWarning = "Lottie expressions were not evaluated; stored property values and keyframes were used.";
        internal static TgsReaderOptions Options => new() { IgnorePuppetEffects = true, IgnoreExpressions = true };
        internal static byte[] Encoded => Create(true, true);

        internal static void Verify()
        {
            foreach (bool unordered in new[] { false, true })
            {
                byte[] input = Create(true, true, unordered);
                byte[] original = input.ToArray();
                TgsDocument document = TgsReader.Read(input, Options);
                TgsDocument reference = TgsReader.Read(Create(false, false));
                if (document.UnsupportedFeatures.Count != 0 || document.Warnings.Count != (unordered ? 3 : 2)
                    || !document.Warnings.Contains(PuppetWarning) || !document.Warnings.Contains(ExpressionWarning))
                {
                    throw new InvalidDataException("Omitted Puppet effects and expressions must each produce one diagnostic.");
                }
                foreach (double frame in new[] { 0d, 5d, 10d, 15d, 20d, 29d })
                {
                    TgsFrame rendered = TgsRenderer.RenderFrameDetailed(document, frame, new() { Supersampling = 1 });
                    using PixelBuffer actual = rendered.Image;
                    using PixelBuffer expected = TgsRenderer.RenderFrame(reference, frame, new() { Supersampling = 1 });
                    if (!actual.Pixels.Span.SequenceEqual(expected.Pixels.Span)
                        || !rendered.Warnings.SequenceEqual(document.Warnings) || !input.SequenceEqual(original))
                    {
                        throw new InvalidDataException("Stored values must render unchanged without evaluating expressions or repairing omitted effect data.");
                    }
                }
            }
            TgsDocument clean = TgsReader.Read(Create(false, false), Options);
            if (clean.Warnings.Count != 0)
            {
                throw new InvalidDataException("Enabling input policies alone must not produce warnings.");
            }
        }

        internal static byte[] Create(bool puppet, bool expression, bool unordered = false)
        {
            string effects = puppet ? """
                [{"ty":34,"mn":"ADBE FreePin3","en":1,"ef":[
                  {"ty":60,"mn":"ADBE FreePin3 ARAP Group","ef":[
                    {"ty":1,"mn":"ADBE FreePin3 Mesh Group","ef":[
                      {"mn":"ADBE FreePin3 Mesh Atom","ef":[
                        {"ty":3,"v":{"a":1,"k":[
                          {"t":10,"s":[5,6]},{"t":0,"s":[1,2]},{"t":null,"s":[3,4]}]}}
                      ]}]}]}]},
                 {"ty":34,"mn":"ADBE FreePin3","en":1,"ef":[{"ty":0,"v":{"k":1}}]}]
                """ : "[]";
            string keys = unordered
                ? "[{\"t\":20,\"s\":[100]},{\"t\":0,\"s\":[0],\"h\":1},{\"t\":10,\"s\":[50],\"h\":1}]"
                : "[{\"t\":0,\"s\":[0],\"h\":1},{\"t\":10,\"s\":[50],\"h\":1},{\"t\":20,\"s\":[100]}]";
            string script = expression ? ",\"x\":\"throw new Error('must not run');\"" : "";
            return Encoding.UTF8.GetBytes("""
                {"v":"5.7","w":8,"h":8,"fr":30,"ip":0,"op":30,"layers":[
                  {"ty":1,"ind":1,"ip":0,"op":30,"sw":8,"sh":8,"sc":"#804020","ef":
                """ + effects + ",\"ks\":{\"o\":{\"a\":1,\"k\":" + keys + script + "}}}]}");
        }
    }
}
