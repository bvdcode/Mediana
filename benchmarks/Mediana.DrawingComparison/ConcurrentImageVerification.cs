// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Security.Cryptography;

namespace Mediana.DrawingComparison
{
    internal static class ConcurrentImageVerification
    {
        internal static int Verify(IDrawingEngine engine, ComparisonScene scene, int parallelism)
        {
            if (parallelism == 1) { return 0; }
            byte[] expected = Hash();
            int count = parallelism * 2;
            Parallel.For(0, count, new() { MaxDegreeOfParallelism = parallelism }, _ =>
            {
                if (!Hash().AsSpan().SequenceEqual(expected))
                {
                    throw new InvalidDataException("Concurrent image differs from the single-context rendering.");
                }
            });
            return count;

            byte[] Hash()
            {
                using IRenderedImage image = engine.Render(scene);
                byte[] pixels = new byte[checked(scene.PixelWidth * scene.PixelHeight * 4)];
                image.CopyPixelsTo(pixels);
                return SHA256.HashData(pixels);
            }
        }
    }
}
