// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Documents.OpenXml;
using Mediana.Documents.Word;
using Mediana.Models.Mesh;
using System.Text;

namespace Mediana.Tests
{
    public class MeshIntegrationTests
    {
        [Fact]
        public async Task BinaryStlRecognitionUsesCompleteLengthForLargeSequentialInputs()
        {
            byte[] data = MeshGeometryTestInput.BinaryTriangles(128);
            MeshImageDecoder decoder = new(new() { Width = 32, Height = 32, ShadowMapSize = 64 });
            Assert.False(decoder.CanDecode(data.AsSpan(0, decoder.ProbeLength)));
            Assert.True(decoder.CanDecode(data.AsSpan(0, decoder.ProbeLength), data.Length));
            Assert.False(decoder.CanDecode(data.AsSpan(0, decoder.ProbeLength), data.Length + 1));
            MediaEngine engine = new MediaEngineBuilder().AddImageDecoder(decoder).Build();
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using ShortReadSource source = new(data);
            using (Mediana.Core.Video.VideoFrame frame = await engine.DecodeImageAsync(source, allocator)) { Assert.Equal(32, frame.Format.Width); }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public Task BoundedMeshPreviewsAndFramePublication() => MeshTestInput.VerifyAsync();

        [Fact]
        public async Task MixedRootsAndMismatchedContentTypesCannotSelectAnotherDecoder()
        {
            OpenXmlImageDecoder decoder = new([new WordPageDecoder(), new ThreeMfPageDecoder()]);
            MediaBufferAllocator allocator = new(1024 * 1024);
            await Assert.ThrowsAsync<InvalidDataException>(async () => await decoder.DecodeAsync(MeshTestInput.Package(mixedRoots: true), new(), allocator));
            await Assert.ThrowsAsync<NotSupportedException>(async () => await decoder.DecodeAsync(MeshTestInput.Package(contentType: WordReader.DocumentContentType), new(), allocator));
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task ModelLimitsRejectBeforePreviewAndPackageReadersPreserveTheirOwner()
        {
            byte[] data = MeshTestInput.Package();
            using OpenXmlPackage package = OpenXmlPackage.Open(data);
            ThreeMfDocument document = await ThreeMfReader.ReadAsync(package);
            Assert.Single(document.BuildItems);
            Assert.Equal("3D/main.model", package.CanonicalPart("3d/MAIN.model"));
            await Assert.ThrowsAsync<InvalidDataException>(() => ThreeMfReader.ReadAsync(data, new() { MaxXmlDepth = 2 }));
            await Assert.ThrowsAsync<InvalidDataException>(() => ThreeMfReader.ReadAsync(data, new() { MaxXmlElements = 4 }));
        }

        [Fact]
        public async Task CompleteMemoryInputDoesNotDependOnFilenameHints()
        {
            Assert.Equal(1, (await MeshReader.ReadAsync(MeshGeometryTestInput.BinaryTriangle())).TriangleCount);
            Assert.Equal(1, (await MeshReader.ReadAsync(Encoding.UTF8.GetBytes(MeshTestInput.AsciiTriangle))).TriangleCount);
            Assert.Equal(1, (await MeshReader.ReadAsync(Encoding.UTF8.GetBytes(MeshTestInput.ObjTriangle))).TriangleCount);
            await Assert.ThrowsAsync<NotSupportedException>(() => MeshReader.ReadAsync("unrecognized content"u8.ToArray()));
        }
    }
}
