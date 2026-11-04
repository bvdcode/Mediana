// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;
using Mediana.Images.Tgs;
using System.Text;

namespace Mediana.Tests
{
    internal static class TgsLayerReferenceTestInput
    {
        internal static void VerifyUnreferencedDuplicates()
        {
            TgsDocument document = Read("""
                {"ty":1,"ind":1,"ip":0,"op":30,"sw":4,"sh":8,"sc":"#ff0000"},
                {"ty":1,"ind":1,"ip":0,"op":30,"sw":8,"sh":8,"sc":"#0000ff"}
                """);
            using PixelBuffer image = TgsRenderer.RenderFrame(document, 0, new() { Supersampling = 1 });
            if (image[1, 4] != new Rgba32(255, 0, 0, 255) || image[6, 4] != new Rgba32(0, 0, 255, 255))
            {
                throw new InvalidDataException("Layers with unreferenced duplicate indices must retain their paint order.");
            }
        }

        internal static void VerifyAmbiguousReferences()
        {
            foreach (string reference in new[]
            {
                "\"parent\":1",
                "\"tt\":1,\"tp\":1",
                "\"ef\":[{\"ty\":28,\"ef\":[{\"ty\":10,\"v\":{\"k\":1}}]}]"
            })
            {
                TgsDocument document = Read("""
                    {"ty":1,"ind":1,"ip":0,"op":30,"sw":8,"sh":8,"sc":"#ff0000"},
                    {"ty":1,"ind":1,"ip":0,"op":30,"sw":8,"sh":8,"sc":"#0000ff"},
                    {"ty":1,"ind":3,"ip":0,"op":30,"sw":8,"sh":8,"sc":"#00ff00",
                    """ + reference + "}");
                bool rejected = false;
                try
                {
                    using PixelBuffer image = TgsRenderer.RenderFrame(document, 0, new() { Supersampling = 1 });
                }
                catch (InvalidDataException error) when (error.Message == "Ambiguous Lottie layer reference.")
                {
                    rejected = true;
                }

                if (!rejected)
                {
                    throw new InvalidDataException("An ambiguous parent or matte reference must not select an arbitrary layer.");
                }
            }
        }

        private static TgsDocument Read(string layers) => TgsReader.Read(Encoding.UTF8.GetBytes(
            "{\"v\":\"5.7\",\"w\":8,\"h\":8,\"fr\":30,\"ip\":0,\"op\":30,\"layers\":[" + layers + "]}"));
    }
}
