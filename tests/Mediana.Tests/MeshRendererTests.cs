// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using Mediana.Drawing;
using Mediana.Codecs.Png;
using Mediana.Models.Mesh;

namespace Mediana.Tests
{
    public class MeshRendererTests
    {
        [Fact]
        public async Task RendersStlStreamToPngEndToEnd()
        {
            using MemoryStream source = new(MeshGeometryTestInput.BinaryTriangle());
            TriangleMesh mesh = await StlReader.ReadAsync(source);
            using PixelBuffer image = MeshRenderer.Render(mesh, new MeshRenderOptions { Width = 64, Height = 64, ShadowMapSize = 64 });
            using MemoryStream destination = new();
            Assert.True((image.Pixels.ToArray().Where((_, index) => index % 4 == 3).Count(alpha => alpha > 0)) > (200));
            using Mediana.Core.Video.VideoFrame frame = image.PublishVideoFrame();
            await PngEncoder.EncodeAsync(frame, destination, new Mediana.Core.Memory.MediaBufferAllocator(1024 * 1024));
            Assert.True((destination.Length) > (100));
        }

        [Fact]
        public void DepthTestShowsNearestSurfaceRegardlessOfTriangleOrder()
        {
            Vector3[] vertices = [new(-1, -1, 1), new(1, -1, 1), new(0, 1, 1),
                new(-1, -1, -1), new(1, -1, -0.5f), new(0, 1, -1)];
            MeshRenderOptions options = new()
            {
                Width = 64,
                Height = 64,
                Supersampling = 1,
                CameraDirection = Vector3.UnitZ,
                CameraUp = Vector3.UnitY,
                Shadows = false,
                AmbientOcclusion = false
            };
            using PixelBuffer first = MeshRenderer.Render(new TriangleMesh(vertices, [0, 1, 2, 3, 4, 5]), options);
            using PixelBuffer reversed = MeshRenderer.Render(new TriangleMesh(vertices, [3, 4, 5, 0, 1, 2]), options);
            using PixelBuffer frontOnly = MeshRenderer.Render(new TriangleMesh(vertices.AsSpan(0, 3), [0, 1, 2]), options);
            Assert.Equal(reversed.Pixels.ToArray(), first.Pixels.ToArray());
            Assert.Equal(frontOnly[32, 32], first[32, 32]);
            Assert.Equal(255, first[32, 32].A);
        }

        [Fact]
        public void EqualDepthPreservesFirstTriangleNormalsAcrossTheRow()
        {
            Vector3[] vertices = [new(-1, -1, 0), new(1, -1, 0), new(0, 1, 0)];
            TriangleMesh first = new(vertices, [0, 1, 2], [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ]);
            TriangleMesh second = new(vertices, [0, 1, 2], [Vector3.UnitX, Vector3.UnitX, Vector3.UnitX]);
            TriangleMesh both = new(vertices, [0, 1, 2, 0, 1, 2], [.. first.CornerNormals.Span, .. second.CornerNormals.Span]);
            MeshRenderOptions options = new()
            {
                Width = 67, Height = 39, Supersampling = 1, CameraDirection = Vector3.UnitZ, CameraUp = Vector3.UnitY,
                Shadows = false, AmbientOcclusion = false
            };
            using PixelBuffer expected = MeshRenderer.Render(first, options);
            using PixelBuffer other = MeshRenderer.Render(second, options);
            using PixelBuffer actual = MeshRenderer.Render(both, options);
            Assert.NotEqual(expected[33, 19], other[33, 19]);
            Assert.True(expected.Pixels.Span.SequenceEqual(actual.Pixels.Span));
        }

        [Fact]
        public void FramingHandlesAspectRatioAndVerySmallOrLargeCoordinates()
        {
            Vector3[] vertices = [new(-1, -1, 0), new(1, -1, 0), new(0, 1, 1)];
            MeshRenderOptions options = new() { Width = 120, Height = 60, Shadows = false, AmbientOcclusion = false };
            using PixelBuffer reference = MeshRenderer.Render(new TriangleMesh(vertices, [0, 1, 2]), options);
            byte[] expected = reference.Pixels.ToArray();
            foreach (float scale in new[] { MathF.Pow(2, -90), MathF.Pow(2, 90) })
            {
                Vector3[] transformed = vertices.Select(vertex => (vertex * scale) + new Vector3(4 * scale)).ToArray();
                using PixelBuffer image = MeshRenderer.Render(new TriangleMesh(transformed, [0, 1, 2]), options);
                Assert.Equal(expected, image.Pixels.ToArray());
            }

            int[] covered = Enumerable.Range(0, expected.Length / 4).Where(index => expected[(index * 4) + 3] > 0).ToArray();
            Assert.True((covered.Length) > (100));
            Assert.True((covered.Min(index => index % 120)) > (0));
            Assert.True((covered.Max(index => index % 120)) < (119));
            Assert.True((covered.Min(index => index / 120)) > (0));
            Assert.True((covered.Max(index => index / 120)) < (59));
        }

        [Fact]
        public void SupersamplingProducesTransparentEdgesWithoutDarkFringes()
        {
            TriangleMesh triangle = new([new(-1, -1, 0), new(1, -1, 0), new(0, 1, 0)], [0, 1, 2]);
            using PixelBuffer image = MeshRenderer.Render(triangle, new MeshRenderOptions
            {
                Width = 65,
                Height = 65,
                Supersampling = 4,
                CameraDirection = Vector3.UnitZ,
                CameraUp = Vector3.UnitY,
                Shadows = false,
                AmbientOcclusion = false
            });
            Rgba32 interior = image[32, 32];
            int edges = 0;
            foreach (int index in Enumerable.Range(0, image.Width * image.Height))
            {
                Rgba32 pixel = image[index % image.Width, index / image.Width];
                if (pixel.A is > 0 and < 255)
                {
                    edges++;
                    Assert.InRange(pixel.R, interior.R - 1, interior.R + 1);
                    Assert.InRange(pixel.G, interior.G - 1, interior.G + 1);
                    Assert.InRange(pixel.B, interior.B - 1, interior.B + 1);
                }
            }

            Assert.True((edges) > (20));
            Assert.Equal(0, image[0, 0].A);
        }

        [Fact]
        public void CompositesOverRequestedBackground()
        {
            Rgba32 background = new(21, 43, 65, 128);
            using PixelBuffer image = MeshRenderer.Render(Cube(), new MeshRenderOptions
            {
                Width = 48,
                Height = 48,
                Background = background,
                Shadows = false,
                AmbientOcclusion = false
            });
            Assert.Equal(background, image[0, 0]);
            Assert.Equal(255, image[24, 24].A);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(3)]
        public void OcclusionOutsideTheImageMatchesEmptyDepth(int supersampling)
        {
            TriangleMesh surface = new(
                [new(-1, -1, 0), new(1, -1, 0), new(1, 1, 0), new(-1, 1, 0),
                 new(-1, 0.5f, 0.1f), new(-0.5f, 0.5f, 0.1f), new(-0.5f, 1, 0.1f), new(-1, 1, 0.1f)],
                [0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7]);
            MeshRenderOptions Options(int size, float padding, bool occlusion) => new()
            {
                Width = size, Height = size, Padding = padding, Supersampling = supersampling,
                CameraDirection = Vector3.UnitZ, CameraUp = Vector3.UnitY, Shadows = false, AmbientOcclusion = occlusion
            };
            using PixelBuffer cropped = MeshRenderer.Render(surface, Options(64, 0, true));
            using PixelBuffer padded = MeshRenderer.Render(surface, Options(128, 0.25f, true));
            using PixelBuffer plain = MeshRenderer.Render(surface, Options(64, 0, false));
            Assert.True(DarkerPixels(plain, cropped) > 0);
            for (int y = 0; y < cropped.Height; y++)
            {
                for (int x = 0; x < cropped.Width; x++)
                {
                    Assert.Equal(padded[x + 32, y + 32], cropped[x, y]);
                }
            }
        }

        [Fact]
        public void ShadowsAndOcclusionDarkenRecesses()
        {
            TriangleMesh mesh = CubeWithFloor();
            using PixelBuffer plain = MeshRenderer.Render(mesh, new MeshRenderOptions { Width = 96, Height = 96, Shadows = false, AmbientOcclusion = false });
            using PixelBuffer shadowed = MeshRenderer.Render(mesh, new MeshRenderOptions { Width = 96, Height = 96, ShadowMapSize = 256, AmbientOcclusion = false });
            using PixelBuffer occluded = MeshRenderer.Render(mesh, new MeshRenderOptions { Width = 96, Height = 96, Shadows = false });
            Assert.True((DarkerPixels(plain, shadowed)) > (20));
            Assert.True((DarkerPixels(plain, occluded)) > (5));
        }

        [Fact]
        public void RejectsInvalidGeometryAndUnsafeRenderOptions()
        {
            Assert.Throws<ArgumentException>(() => new TriangleMesh([new(float.NaN, 0, 0)], [0, 0, 0]));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TriangleMesh([Vector3.Zero], [0, 1, 2]));
            TriangleMesh collapsed = new([Vector3.Zero, Vector3.UnitX, Vector3.UnitX * 2], [0, 1, 2]);
            Assert.Throws<ArgumentException>(() => MeshRenderer.Render(collapsed));
            Assert.Throws<ArgumentOutOfRangeException>(() => MeshRenderer.Render(Cube(), new MeshRenderOptions { Width = int.MaxValue, Height = int.MaxValue }));
            Assert.Throws<ArgumentException>(() => MeshRenderer.Render(Cube(), new MeshRenderOptions { CameraDirection = Vector3.UnitZ, CameraUp = Vector3.UnitZ }));
            Assert.Throws<ArgumentOutOfRangeException>(() => MeshRenderer.Render(Cube(), new MeshRenderOptions { Exposure = float.NaN }));
            Assert.Throws<ArgumentOutOfRangeException>(() => MeshRenderer.Render(Cube(), new MeshRenderOptions { Supersampling = 0 }));
        }

        [Fact]
        public void MeshOwnsItsInputAndRenderingHonorsCancellation()
        {
            Vector3[] vertices = [Vector3.Zero, Vector3.UnitX, Vector3.UnitY];
            int[] indices = [0, 1, 2];
            TriangleMesh mesh = new(vertices, indices);
            vertices[1] = Vector3.Zero;
            indices[2] = 0;
            Assert.Equal(Vector3.UnitX, mesh.Vertices.Span[1]);
            Assert.Equal(2, mesh.TriangleIndices.Span[2]);
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();
            Assert.Throws<OperationCanceledException>(() => MeshRenderer.Render(mesh, cancellationToken: cancellation.Token));
        }

        [Fact]
        public void IgnoresDegenerateFacesMixedWithValidGeometry()
        {
            Vector3[] vertices = [Vector3.Zero, Vector3.UnitX, Vector3.UnitY];
            TriangleMesh valid = new(vertices, [0, 1, 2]);
            TriangleMesh mixed = new(vertices, [0, 1, 2, 0, 0, 1]);
            MeshRenderOptions options = new() { Width = 48, Height = 48, ShadowMapSize = 64 };
            using PixelBuffer a = MeshRenderer.Render(valid, options);
            using PixelBuffer b = MeshRenderer.Render(mixed, options);
            Assert.Equal(a.Pixels.ToArray(), b.Pixels.ToArray());
        }

        [Fact]
        public void ModelRotationMatchesRotatedGeometryAndSourceNormalsCanBeOverridden()
        {
            Vector3[] vertices = [new(-1, -1, 0), new(1, -1, 0), new(0, 1, 0)];
            Vector3[] normals = [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ];
            Quaternion rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2);
            TriangleMesh source = new(vertices, [0, 1, 2], normals);
            TriangleMesh rotated = new(vertices.Select(vertex => Vector3.Transform(vertex, rotation)).ToArray(), [0, 1, 2],
                normals.Select(normal => Vector3.Transform(normal, rotation)).ToArray());
            using PixelBuffer actual = MeshRenderer.Render(source, new MeshRenderOptions { Width = 64, Height = 64, Shadows = false, AmbientOcclusion = false, ModelRotation = rotation });
            using PixelBuffer expected = MeshRenderer.Render(rotated, new MeshRenderOptions { Width = 64, Height = 64, Shadows = false, AmbientOcclusion = false });
            Assert.Equal(expected.Pixels.ToArray(), actual.Pixels.ToArray());

            TriangleMesh unusualNormals = new(vertices, [0, 1, 2], [Vector3.UnitX, Vector3.UnitX, Vector3.UnitX]);
            MeshRenderOptions generated = new() { Width = 64, Height = 64, Shadows = false, AmbientOcclusion = false, UseSourceNormals = false };
            using PixelBuffer a = MeshRenderer.Render(source, generated);
            using PixelBuffer b = MeshRenderer.Render(unusualNormals, generated);
            Assert.Equal(a.Pixels.ToArray(), b.Pixels.ToArray());
            Assert.Throws<ArgumentException>(() => MeshRenderer.Render(source, new MeshRenderOptions { ModelRotation = new Quaternion() }));
        }

        [Theory]
        [InlineData(64, false)]
        [InlineData(65, true)]
        public void SmoothingUsesFaceNormalsBeyondTheNeighborLimit(int faces, bool matchesFlat)
        {
            Vector3[] vertices = new Vector3[faces * 3];
            for (int face = 0; face < faces; face++)
            {
                float angle = face * MathF.Tau / faces;
                float end = (face + 0.8f) * MathF.Tau / faces;
                vertices[face * 3] = Vector3.UnitZ;
                vertices[(face * 3) + 1] = new(MathF.Cos(angle), MathF.Sin(angle), 0);
                vertices[(face * 3) + 2] = new(MathF.Cos(end), MathF.Sin(end), 0);
            }
            TriangleMesh fan = new(vertices, Enumerable.Range(0, vertices.Length).ToArray());
            MeshRenderOptions Options(bool smooth) => new()
            {
                Width = 128, Height = 128, Supersampling = 1, Shadows = false, AmbientOcclusion = false,
                SmoothNormals = smooth, CreaseAngle = 180, CameraDirection = Vector3.UnitZ, CameraUp = Vector3.UnitY
            };
            using PixelBuffer smooth = MeshRenderer.Render(fan, Options(true));
            using PixelBuffer flat = MeshRenderer.Render(fan, Options(false));
            Assert.Equal(matchesFlat, smooth.Pixels.Span.SequenceEqual(flat.Pixels.Span));
        }

        private static int DarkerPixels(PixelBuffer reference, PixelBuffer shaded)
        {
            int count = 0;
            for (int y = 0; y < reference.Height; y++)
            {
                for (int x = 0; x < reference.Width; x++)
                {
                    Rgba32 a = reference[x, y];
                    Rgba32 b = shaded[x, y];
                    if (a.A == 255 && b.R + b.G + b.B < (a.R + a.G + a.B) - 3)
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        private static TriangleMesh Cube() => new(
            [new(-0.5f, -0.5f, 0), new(0.5f, -0.5f, 0), new(0.5f, 0.5f, 0), new(-0.5f, 0.5f, 0),
             new(-0.5f, -0.5f, 1), new(0.5f, -0.5f, 1), new(0.5f, 0.5f, 1), new(-0.5f, 0.5f, 1)],
            [0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4, 1, 2, 6, 1, 6, 5, 2, 3, 7, 2, 7, 6, 3, 0, 4, 3, 4, 7]);

        private static TriangleMesh CubeWithFloor()
        {
            TriangleMesh cube = Cube();
            Vector3[] vertices = cube.Vertices.ToArray().Concat(new Vector3[] { new(-2, -2, 0), new(2, -2, 0), new(2, 2, 0), new(-2, 2, 0) }).ToArray();
            int[] indices = cube.TriangleIndices.ToArray().Concat(new[] { 8, 9, 10, 8, 10, 11 }).ToArray();
            return new TriangleMesh(vertices, indices);
        }
    }
}
