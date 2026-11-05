// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using System.Text;
using Mediana.Drawing;
using Mediana.Codecs.Png;
using Mediana.Models.Mesh;

namespace Mediana.Tests
{
    public class ObjReaderTests
    {
        [Fact]
        public async Task TriangulatesConcavePolygonsWithNegativeIndicesAndContinuations()
        {
            using MemoryStream source = Text("""
                v 0 0 0
                v 3 0 0
                v 3 3 0
                v 1 1 0
                v 0 3 0
                o concave
                g first
                f -5 -4 -3 \
                   -2 -1 # comment
                """);
            TriangleMesh mesh = await ObjReader.ReadAsync(source);
            Assert.Equal(3, mesh.TriangleCount);
            float area = 0;
            for (int corner = 0; corner < mesh.TriangleIndices.Length; corner += 3)
            {
                Vector3 a = mesh.Vertices.Span[mesh.TriangleIndices.Span[corner]];
                Vector3 b = mesh.Vertices.Span[mesh.TriangleIndices.Span[corner + 1]];
                Vector3 c = mesh.Vertices.Span[mesh.TriangleIndices.Span[corner + 2]];
                float signedArea = Vector3.Cross(b - a, c - a).Z * 0.5f;
                Assert.True((signedArea) > (0));
                area += signedArea;
            }

            Assert.InRange(area, 6 - 0.0001f, 6 + 0.0001f);
            Assert.True(source.CanRead);
        }

        [Fact]
        public async Task PreservesIndexedNormalsAndHonorsSmoothingOff()
        {
            using MemoryStream source = Text("""
                v 0 0 0
                v 1 0 0
                v 0 1 0
                v 0 0 1
                vn 0 3 0
                vt 0 0
                vt 1 0
                vt 0 1
                f 1/1/1 2/2/1 3/3/1
                s off
                f 1 4 2
                """);
            TriangleMesh mesh = await ObjReader.ReadAsync(source);
            Assert.Equal(Vector3.UnitY, mesh.CornerNormals.Span[0]);
            Assert.Equal(Vector3.UnitY, mesh.CornerNormals.Span[3]);
        }

        [Fact]
        public async Task ExplicitSmoothingGroupsShareNormalsOnlyWithinGroup()
        {
            using MemoryStream source = Text("""
                v 0 0 0
                v 1 0 0
                v 0 1 0
                v 0 0 1
                s 1
                f 1 2 3
                f 1 4 2
                s 2
                f 1 3 4
                """);
            TriangleMesh mesh = await ObjReader.ReadAsync(source);
            Assert.Equal(Vector3.Normalize(new Vector3(0, 1, 1)), mesh.CornerNormals.Span[0]);
            Assert.Equal(Vector3.UnitX, mesh.CornerNormals.Span[6]);
        }

        [Theory]
        [InlineData("f 0 2 3")]
        [InlineData("f 1 2 4")]
        [InlineData("f -4 -2 -1")]
        [InlineData("f 1//1 2//1 3//1")]
        [InlineData("f 1/1 2/2 3/3")]
        [InlineData("v NaN 0 0\nf 1 2 3")]
        public async Task RejectsInvalidIndicesAndNonFiniteValues(string face)
        {
            using MemoryStream source = Text("v 0 0 0\nv 1 0 0\nv 0 1 0\n" + face);
            await Assert.ThrowsAsync<InvalidDataException>(async () => await ObjReader.ReadAsync(source));
        }

        [Fact]
        public async Task RejectsSelfIntersectingPolygonsAndEnforcesLimits()
        {
            await Assert.ThrowsAsync<InvalidDataException>(async () => await ObjReader.ReadAsync(Text("v 0 0 0\nv 1 1 0\nv 0 1 0\nv 1 0 0\nf 1 2 3 4")));
            const string triangle = "v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\nf 1 2 3";
            await Assert.ThrowsAsync<InvalidDataException>(async () => await ObjReader.ReadAsync(Text(triangle), new ObjReaderOptions { MaxTriangleCount = 1 }));
            await Assert.ThrowsAsync<InvalidDataException>(async () => await ObjReader.ReadAsync(Text(triangle), new ObjReaderOptions { MaxVertexCount = 2 }));
            await Assert.ThrowsAsync<InvalidDataException>(async () => await ObjReader.ReadAsync(Text(triangle), new ObjReaderOptions { MaxInputBytes = 5 }));
        }

        [Fact]
        public async Task SupportsCancellationAndRejectsUntessellatedSurfaces()
        {
            using CancellationTokenSource cancellation = new();
            await cancellation.CancelAsync();
            await Assert.ThrowsAsync<OperationCanceledException>(async () => await ObjReader.ReadAsync(Text(""), cancellationToken: cancellation.Token));
            await Assert.ThrowsAsync<NotSupportedException>(async () => await ObjReader.ReadAsync(Text("surf 0 1 0 1 1 2 3 4")));
        }

        private static MemoryStream Text(string text) => new(Encoding.UTF8.GetBytes(text));
    }
}
