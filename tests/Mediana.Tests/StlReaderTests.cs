// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using Mediana.Drawing;
using Mediana.Codecs.Png;
using Mediana.Models.Mesh;

namespace Mediana.Tests
{
    public class StlReaderTests
    {
        private const string AsciiTriangle = """
            solid triangle
              facet normal 0 0 1
                outer loop
                  vertex 0 0 0
                  vertex 1e0 0 0
                  vertex 0 1 0
                endloop
              endfacet
            endsolid triangle
            """;

        [Fact]
        public async Task ReadsAsciiWithBomMultipleSolidsAndInvariantNumbers()
        {
            using MemoryStream stream = new(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(AsciiTriangle + "\n" + AsciiTriangle)).ToArray());
            TriangleMesh mesh = await StlReader.ReadAsync(stream);
            Assert.Equal(2, mesh.TriangleCount);
            Assert.Equal(Vector3.UnitX, mesh.Vertices.Span[1]);
            Assert.True(stream.CanRead);
        }

        [Fact]
        public async Task BinaryHeaderStartingWithSolidIsStillBinary()
        {
            using MemoryStream stream = new(MeshGeometryTestInput.BinaryTriangle());
            TriangleMesh mesh = await StlReader.ReadAsync(stream);
            Assert.Equal(1, mesh.TriangleCount);
            Assert.Equal(Vector3.UnitY, mesh.Vertices.Span[2]);
        }

        [Fact]
        public async Task ReadsFromCurrentPositionOnMeshNonSeekableStream()
        {
            using MemoryStream inner = new(new byte[7].Concat(MeshGeometryTestInput.BinaryTriangle()).ToArray());
            inner.Position = 7;
            using MeshNonSeekableStream source = new(inner);
            TriangleMesh mesh = await StlReader.ReadAsync(source);
            Assert.Equal(1, mesh.TriangleCount);
            Assert.True(inner.CanRead);
        }

        [Fact]
        public async Task RejectsTruncatedBinaryAndNonFiniteCoordinates()
        {
            byte[] bytes = MeshGeometryTestInput.BinaryTriangle();
            await Assert.ThrowsAsync<InvalidDataException>(async () => await StlReader.ReadAsync(new MemoryStream(bytes[..^1])));
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(96), float.NaN);
            await Assert.ThrowsAsync<InvalidDataException>(async () => await StlReader.ReadAsync(new MemoryStream(bytes)));
        }

        [Theory]
        [InlineData("endsolid triangle", "")]
        [InlineData("endfacet", "invalid")]
        [InlineData("vertex 1e0 0 0", "vertex NaN 0 0")]
        [InlineData("vertex 1e0 0 0", "vertex 1,5 0 0")]
        public async Task RejectsMalformedAscii(string oldValue, string newValue)
        {
            using MemoryStream stream = new(Encoding.UTF8.GetBytes(AsciiTriangle.Replace(oldValue, newValue)));
            await Assert.ThrowsAsync<InvalidDataException>(async () => await StlReader.ReadAsync(stream));
        }

        [Fact]
        public async Task EnforcesByteAndTriangleLimitsForBothFormats()
        {
            foreach (byte[] data in new[] { MeshGeometryTestInput.BinaryTriangle(), Encoding.UTF8.GetBytes(AsciiTriangle) })
            {
                await Assert.ThrowsAsync<InvalidDataException>(async () =>
                    await StlReader.ReadAsync(new MeshNonSeekableStream(new MemoryStream(data)), new StlReaderOptions { MaxInputBytes = 32 }));
            }

            using MemoryStream multiple = new(Encoding.UTF8.GetBytes(AsciiTriangle + "\n" + AsciiTriangle));
            await Assert.ThrowsAsync<InvalidDataException>(async () => await StlReader.ReadAsync(multiple, new StlReaderOptions { MaxTriangleCount = 1 }));
            byte[] twoBinary = new byte[184];
            MeshGeometryTestInput.BinaryTriangle().CopyTo(twoBinary, 0);
            MeshGeometryTestInput.BinaryTriangle().AsSpan(84).CopyTo(twoBinary.AsSpan(134));
            BinaryPrimitives.WriteUInt32LittleEndian(twoBinary.AsSpan(80), 2);
            await Assert.ThrowsAsync<InvalidDataException>(async () => await StlReader.ReadAsync(new MemoryStream(twoBinary), new StlReaderOptions { MaxTriangleCount = 1 }));
        }

        [Fact]
        public async Task HonorsCancellation()
        {
            using CancellationTokenSource cancellation = new();
            await cancellation.CancelAsync();
            await Assert.ThrowsAsync<OperationCanceledException>(async () => await StlReader.ReadAsync(new MemoryStream(MeshGeometryTestInput.BinaryTriangle()), cancellationToken: cancellation.Token));
        }

    }
}
