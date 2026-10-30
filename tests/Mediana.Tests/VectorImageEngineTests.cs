// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Security.Cryptography;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Documents.Pdf;
using Mediana.Images.Svg;
using Mediana.Images.Tgs;
using Mediana.IO;

namespace Mediana.Tests
{
    public class VectorImageEngineTests
    {
        [Fact]
        public async Task RegisteredVectorAnimationAndPageOutputMatchesIndependentReferences() =>
            await VectorImageEngineTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data"));

        [Fact]
        public async Task IndependentVectorAndPageRequestsCanShareOneRegistryConcurrently()
        {
            MediaEngine engine = new MediaEngineBuilder()
                .AddImageDecoder(new SvgImageDecoder(renderOptions: new() { Width = 32, Supersampling = 1 }))
                .AddImageDecoder(new TgsImageDecoder(renderOptions: new() { Width = 32, Supersampling = 1 }))
                .AddImageDecoder(new PdfImageDecoder(renderOptions: new() { MaximumWidth = 32, Supersampling = 1 })).Build();
            byte[][] inputs = new byte[3][];
            (string Folder, string Name)[] files = [("Svg", "vector-preview.svg"), ("Tgs", "geometry.tgs"), ("Pdf", "graphics.pdf")];
            MediaBufferAllocator allocator = new(1024 * 1024);
            string[] expected = new string[3];
            for (int index = 0; index < inputs.Length; index++)
            {
                inputs[index] = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", files[index].Folder, files[index].Name));
                using VideoFrame frame = await DecodeAsync(inputs[index]);
                expected[index] = Convert.ToHexStringLower(SHA256.HashData(frame.Data.Span));
            }
            Task[] requests = Enumerable.Range(0, 18).Select(index => Task.Run(async () =>
            {
                using VideoFrame frame = await DecodeAsync(inputs[index % inputs.Length]);
                Assert.Equal(expected[index % inputs.Length], Convert.ToHexStringLower(SHA256.HashData(frame.Data.Span)));
            })).ToArray();
            await Task.WhenAll(requests);
            Assert.Equal(0, allocator.AllocatedBytes);

            async Task<VideoFrame> DecodeAsync(byte[] encoded)
            {
                using MemoryStream memory = new(encoded);
                await using RandomAccessStreamByteSource source = new(memory);
                return await engine.DecodeImageAsync(source, allocator);
            }
        }
    }
}
