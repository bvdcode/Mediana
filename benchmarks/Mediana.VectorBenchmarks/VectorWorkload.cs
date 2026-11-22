// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;
using Mediana.Images.Svg;
using Mediana.Images.Tgs;

namespace Mediana.VectorBenchmarks
{
    internal class VectorWorkload(byte[] encoded, SvgDocument? svg, TgsDocument? tgs, DrawingRenderOptions options, double position) : IDisposable
    {
        internal static async Task<VectorWorkload> ReadAsync(string path, int size, int scale, double position)
        {
            byte[] encoded = await File.ReadAllBytesAsync(path);
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".svg":
                    SvgDocument svg = await SvgReader.ReadAsync(encoded);
                    double fit = size / Math.Max(svg.Width, svg.Height);
                    return new(encoded, svg, null, new() { Width = (int)Math.Round(svg.Width * fit), Height = (int)Math.Round(svg.Height * fit), Supersampling = scale }, position);
                case ".tgs":
                case ".json":
                    return new(encoded, null, TgsReader.Read(encoded), new() { Width = size, Height = size, Supersampling = scale }, position);
                default: throw new ArgumentException("Expected SVG, TGS or Lottie JSON.", nameof(path));
            }
        }

        internal async ValueTask<PixelBuffer> RenderAsync(bool read)
        {
            using SvgDocument? parsed = read && svg is not null ? await SvgReader.ReadAsync(encoded) : null;
            if (svg is not null) { return SvgRenderer.Render(parsed ?? svg, options); }
            TgsDocument animation = read ? TgsReader.Read(encoded) : tgs!;
            return TgsRenderer.RenderFrame(animation, animation.InPoint + (animation.OutPoint - animation.InPoint) * position, options);
        }

        public void Dispose() => svg?.Dispose();
    }
}
