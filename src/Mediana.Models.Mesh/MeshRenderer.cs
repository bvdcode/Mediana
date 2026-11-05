// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;
using Mediana.Core.Memory;
using System;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace Mediana.Models.Mesh
{
    /// <summary>Renders opaque triangle meshes on the CPU using managed .NET code.</summary>
    public static class MeshRenderer
    {
        /// <summary>Renders an automatically framed orthographic preview with a transparent background by default.</summary>
        /// <remarks>Rendering is synchronous and cancellable. The input mesh is not modified.</remarks>
        public static PixelBuffer Render(TriangleMesh mesh, MeshRenderOptions? options = null, CancellationToken cancellationToken = default, MediaBufferAllocator? allocator = null)
        {
            ArgumentNullException.ThrowIfNull(mesh);
            options ??= new MeshRenderOptions();
            options.Validate();
            cancellationToken.ThrowIfCancellationRequested();
            allocator ??= new MediaBufferAllocator(options.MaximumWorkingBytes);
            using PreparedMesh prepared = new(mesh, options, allocator, cancellationToken);
            int width = options.Width * options.Supersampling;
            int height = options.Height * options.Supersampling;
            RasterCamera camera = new(prepared, options.CameraDirection, options.CameraUp, width, height, options.Padding);
            using MeshBuffer<Vector3> normals = new(checked(width * height), allocator);
            using MeshBuffer<float> depths = new(checked(width * height), allocator);
            TriangleRasterizer.Draw(prepared, camera, depths.Span, normals.Span, allocator, cancellationToken);
            RasterCamera? shadowCamera = null;
            using MeshBuffer<float>? shadowDepths = options.Shadows ? new(checked(options.ShadowMapSize * options.ShadowMapSize), allocator) : null;
            if (options.Shadows)
            {
                shadowCamera = new RasterCamera(prepared, StudioLighting.KeyDirection, Vector3.UnitZ,
                    options.ShadowMapSize, options.ShadowMapSize, 0.03f);
                TriangleRasterizer.Draw(prepared, shadowCamera, shadowDepths!.Span, Span<Vector3>.Empty, allocator, cancellationToken);
            }

            StudioLighting lighting = new(options, shadowCamera, shadowDepths);
            PixelBuffer output = new(options.Width, options.Height, allocator);
            bool completed = false;
            try
            {
                Vector3 background = new(StudioLighting.ToLinear(options.Background.R / 255f),
                    StudioLighting.ToLinear(options.Background.G / 255f), StudioLighting.ToLinear(options.Background.B / 255f));
                float backgroundAlpha = options.Background.A / 255f;
                Parallel.For(0, options.Height, new ParallelOptions { CancellationToken = cancellationToken, MaxDegreeOfParallelism = options.MaximumDegreeOfParallelism }, y =>
                {
                    Span<byte> pixels = output.Pixels.Span;
                    ReadOnlySpan<float> rowDepths = depths.Span;
                    ReadOnlySpan<Vector3> rowNormals = normals.Span;
                    for (int x = 0; x < options.Width; x++)
                    {
                        if ((x & 63) == 0)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                        }

                        Vector3 sum = Vector3.Zero;
                        float alpha = 0;
                        for (int sy = 0; sy < options.Supersampling; sy++)
                        {
                            for (int sx = 0; sx < options.Supersampling; sx++)
                            {
                                int sampleX = (x * options.Supersampling) + sx;
                                int sampleY = (y * options.Supersampling) + sy;
                                int offset = (sampleY * width) + sampleX;
                                float depth = rowDepths[offset];
                                if (!float.IsFinite(depth))
                                {
                                    sum += background * backgroundAlpha;
                                    alpha += backgroundAlpha;
                                    continue;
                                }

                                Vector3 point = camera.Unproject(sampleX + 0.5f, sampleY + 0.5f, depth);
                                float occlusion = options.AmbientOcclusion
                                    ? ScreenSpaceOcclusion.Evaluate(sampleX, sampleY, point, rowNormals[offset], camera, rowDepths) : 1;
                                sum += lighting.Shade(point, rowNormals[offset], camera.ViewDirection, occlusion);
                                alpha += 1;
                            }
                        }

                        int target = ((y * options.Width) + x) * 4;
                        if (alpha > 0)
                        {
                            // Average premultiplied linear samples, then return straight sRGB pixels.
                            Vector3 color = sum / alpha;
                            pixels[target] = StudioLighting.ToSrgbByte(color.X);
                            pixels[target + 1] = StudioLighting.ToSrgbByte(color.Y);
                            pixels[target + 2] = StudioLighting.ToSrgbByte(color.Z);
                            pixels[target + 3] = (byte)MathF.Round((alpha * 255) / (options.Supersampling * options.Supersampling));
                        }
                    }
                });

                cancellationToken.ThrowIfCancellationRequested();
                completed = true;
                return output;
            }
            finally
            {
                if (!completed) { output.Dispose(); }
            }
        }
    }
}
