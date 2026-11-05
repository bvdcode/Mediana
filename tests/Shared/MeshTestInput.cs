// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Codecs.Png;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Documents.OpenXml;
using Mediana.Drawing;
using Mediana.Models.Mesh;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;

namespace Mediana.Tests
{
    internal static class MeshTestInput
    {
        internal const string AsciiTriangle = "solid sample\nfacet normal 0 0 1\nouter loop\nvertex 0 0 0\nvertex 1 0 0\nvertex 0 1 0\nendloop\nendfacet\nendsolid sample\n";
        internal const string ObjTriangle = "# authored polygon\nv 0 0 0\nv 1 0 0\nv 0 1 0\nf -3 -2 -1\n";
        internal const string Model = "<model xmlns='http://schemas.microsoft.com/3dmanufacturing/core/2015/02'><resources><object id='1'><mesh><vertices><vertex x='0' y='0' z='0'/><vertex x='1' y='0' z='0'/><vertex x='0' y='1' z='0'/></vertices><triangles><triangle v1='0' v2='1' v3='2'/></triangles></mesh></object></resources><build><item objectid='1'/></build></model>";
        private const string ModelRelationship = "http://schemas.microsoft.com/3dmanufacturing/2013/01/3dmodel";

        internal static byte[] Package(string? model = null, string? contentType = null, bool mixedRoots = false, IReadOnlyDictionary<string, string>? additionalParts = null)
        {
            using MemoryStream output = new();
            using (ZipArchive archive = new(output, ZipArchiveMode.Create, leaveOpen: true))
            {
                Write(archive, "[Content_Types].xml", "<Types xmlns='http://schemas.openxmlformats.org/package/2006/content-types'><Default Extension='rels' ContentType='application/vnd.openxmlformats-package.relationships+xml'/><Default Extension='model' ContentType='" + (contentType ?? ThreeMfReader.ModelContentType) + "'/></Types>");
                string other = mixedRoots ? "<Relationship Id='word' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument' Target='/3D/main.model'/>" : "";
                Write(archive, "_rels/.rels", "<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'><Relationship Id='model' Type='" + ModelRelationship + "' Target='/3d/MAIN.model'/>" + other + "</Relationships>");
                Write(archive, "3D/main.model", model ?? Model);
                if (additionalParts is not null)
                {
                    foreach (KeyValuePair<string, string> part in additionalParts) { Write(archive, part.Key, part.Value); }
                }
            }
            return output.ToArray();
        }

        internal static byte[] ProductionPackage() => Package(
            "<model xmlns='http://schemas.microsoft.com/3dmanufacturing/core/2015/02' xmlns:p='http://schemas.microsoft.com/3dmanufacturing/production/2015/06' requiredextensions='p'><resources/><build><item objectid='1' p:path='/3D/part.model'/></build></model>",
            additionalParts: new Dictionary<string, string>
            {
                ["3D/part.model"] = Model,
                ["3D/_rels/main.model.rels"] = "<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'><Relationship Id='part' Type='" + ModelRelationship + "' Target='/3D/part.model'/></Relationships>"
            });

        internal static async Task VerifyAsync()
        {
            byte[][] inputs = [MeshGeometryTestInput.BinaryTriangle(), Encoding.UTF8.GetBytes(AsciiTriangle), Encoding.UTF8.GetBytes(ObjTriangle), Package(), ProductionPackage()];
            foreach (byte[] encoded in inputs)
            {
                TriangleMesh mesh = await MeshReader.ReadAsync(encoded);
                Require(mesh.TriangleCount == 1 && mesh.Vertices.Span[1] == Vector3.UnitX);
                encoded.AsSpan().Clear();
                MediaBufferAllocator allocator = new(1024 * 1024);
                using (PixelBuffer pixels = MeshRenderer.Render(mesh, new() { Width = 64, Height = 64, ShadowMapSize = 64 }, allocator: allocator))
                {
                    Require(allocator.AllocatedBytes == 64 * 64 * 4);
                    ReadOnlyMemory<byte> borrowed = pixels.Pixels;
                    byte[] expected = borrowed.ToArray();
                    using VideoFrame frame = pixels.PublishVideoFrame();
                    Require(MemoryMarshal.TryGetArray(borrowed, out ArraySegment<byte> before));
                    Require(MemoryMarshal.TryGetArray(frame.Data, out ArraySegment<byte> after));
                    Require(ReferenceEquals(before.Array, after.Array) && before.Offset == after.Offset);
                    pixels.Dispose();
                    Require(allocator.AllocatedBytes == 64 * 64 * 4 && frame.Data.Span.SequenceEqual(expected));
                    using MemoryStream png = new();
                    await PngEncoder.EncodeAsync(frame, png, allocator);
                    using VideoFrame decoded = await new PngImageDecoder().DecodeAsync(png.ToArray(), new(), allocator);
                    Require(decoded.Data.Span.SequenceEqual(expected));
                }
                Require(allocator.AllocatedBytes == 0);
            }
            ThreeMfDocument assembly = await ThreeMfReader.ReadAsync(ProductionPackage());
            Require(assembly.RootModelPart == "3D/main.model" && assembly.Objects.Count == 1);
            Require(assembly.CreateBuildItemMesh(0).TriangleCount == 1);
            Require(assembly.CreateObjectMesh(1, "3D/PART.model").TriangleCount == 1);
            VerifyBudgets();
            await VerifyRegistrationAsync();
            await VerifyConcurrencyAndCancellationAsync();
        }

        private static void VerifyBudgets()
        {
            TriangleMesh mesh = new([Vector3.Zero, Vector3.UnitX, Vector3.UnitY], [0, 1, 2]);
            MeshRenderOptions options = new() { Width = 65, Height = 65, ShadowMapSize = 64 };
            foreach (int maximum in new[] { 1, 36, 84, 128, 2048, 200_000, 270_000, 290_000, 300_000 })
            {
                MediaBufferAllocator allocator = new(maximum + 16);
                using WritableMediaBuffer existing = allocator.Allocate(16);
                bool rejected = false;
                try
                {
                    using PixelBuffer unexpected = MeshRenderer.Render(mesh, options, allocator: allocator);
                }
                catch (MediaMemoryLimitException exception)
                {
                    System.Diagnostics.Trace.TraceInformation("Expected mesh budget rejection: {0}", exception.Message);
                    rejected = true;
                }
                Require(rejected && allocator.AllocatedBytes == 16);
            }
            MediaBufferAllocator limit = new(1024 * 1024);
            TriangleMesh degenerate = new([Vector3.Zero, Vector3.UnitX, Vector3.UnitX * 2], [0, 1, 2]);
            bool invalid = false;
            try
            {
                using PixelBuffer unexpected = MeshRenderer.Render(degenerate, options, allocator: limit);
            }
            catch (ArgumentException exception)
            {
                System.Diagnostics.Trace.TraceInformation("Expected degenerate geometry rejection: {0}", exception.Message);
                invalid = true;
            }
            Require(invalid && limit.AllocatedBytes == 0);
        }

        internal static async Task VerifyRegistrationAsync()
        {
            MediaEngine engine = new MediaEngineBuilder().AddImageDecoder(new MeshImageDecoder(new() { Width = 48, Height = 32, ShadowMapSize = 64 }))
                .AddImageDecoder(new OpenXmlImageDecoder([new Mediana.Documents.Word.WordPageDecoder(), new ThreeMfPageDecoder(renderOptions: new() { Width = 48, Height = 32, ShadowMapSize = 64 })])).Build();
            MediaBufferAllocator allocator = new(2 * 1024 * 1024);
            foreach (byte[] data in new[] { MeshGeometryTestInput.BinaryTriangle(), Encoding.UTF8.GetBytes(AsciiTriangle), Encoding.UTF8.GetBytes(ObjTriangle), Package(), ProductionPackage() })
            {
                using (VideoFrame frame = await engine.DecodeImageAsync(data, allocator))
                {
                    Require(frame.Format.Width == 48 && frame.Format.Height == 32);
                }
                await using Mediana.IO.StreamByteSource source = new(new MemoryStream(data));
                MediaInfo info = await engine.ProbeAsync(source);
                Require(info.Image is { IsPreviewCanvas: true, Width: 48, Height: 32 });
            }
            Require(allocator.AllocatedBytes == 0);
            byte[] word = WordTestInput.Create("<w:p><w:r><w:t>Word alongside 3MF</w:t></w:r></w:p>", WordRenderTestInput.Fonts, page: WordRenderTestInput.SmallPage);
            using (VideoFrame frame = await engine.DecodeImageAsync(word, allocator)) { Require(frame.Format.Width == 160 && frame.Format.Height == 160); }
            Require(allocator.AllocatedBytes == 0);
        }

        private static async Task VerifyConcurrencyAndCancellationAsync()
        {
            TriangleMesh mesh = new([Vector3.Zero, Vector3.UnitX, Vector3.UnitY], [0, 1, 2]);
            MeshRenderOptions options = new() { Width = 80, Height = 60, ShadowMapSize = 64, MaximumDegreeOfParallelism = 1 };
            MediaBufferAllocator allocator = new(32 * 1024 * 1024);
            Task<PixelBuffer> first = Task.Run(() => MeshRenderer.Render(mesh, options, allocator: allocator));
            Task<PixelBuffer> second = Task.Run(() => MeshRenderer.Render(mesh, options, allocator: allocator));
            PixelBuffer[] outputs = await Task.WhenAll(first, second);
            using (outputs[0])
            using (outputs[1])
            using (PixelBuffer parallel = MeshRenderer.Render(mesh, new() { Width = 80, Height = 60, ShadowMapSize = 64, MaximumDegreeOfParallelism = 2 }, allocator: allocator))
            {
                Require(outputs[0].Pixels.Span.SequenceEqual(outputs[1].Pixels.Span));
                Require(parallel.Pixels.Span.SequenceEqual(outputs[0].Pixels.Span));
                Require(allocator.AllocatedBytes == 3 * 80 * 60 * 4);
            }
            Require(allocator.AllocatedBytes == 0);
            using CancellationTokenSource cancellation = new();
            Task<PixelBuffer> pending = Task.Run(() => MeshRenderer.Render(mesh, new() { Width = 512, Height = 512 }, cancellation.Token, allocator));
            while (!pending.IsCompleted && allocator.AllocatedBytes == 0) { await Task.Yield(); }
            await cancellation.CancelAsync();
            try
            {
                using PixelBuffer completed = await pending;
            }
            catch (OperationCanceledException exception)
            {
                System.Diagnostics.Trace.TraceInformation("Expected mesh cancellation: {0}", exception.Message);
            }
            Require(allocator.AllocatedBytes == 0);
        }

        private static void Write(ZipArchive archive, string name, string text)
        {
            using Stream output = archive.CreateEntry(name).Open();
            output.Write(Encoding.UTF8.GetBytes(text));
        }

        private static void Require(bool condition)
        {
            if (!condition) { throw new InvalidDataException("Mesh geometry, ownership, package registration or resource accounting check failed."); }
        }
    }
}
