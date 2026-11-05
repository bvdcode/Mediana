// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Text;

namespace Mediana.Tests
{
    [Collection("CommandLine")]
    public class CliMeshTests : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("mediana-mesh-").FullName;

        [Theory]
        [InlineData("binary-stl")]
        [InlineData("ascii-stl")]
        [InlineData("obj")]
        [InlineData("3mf")]
        public async Task ConvertsGeometryByContentWithAnArbitraryExtension(string format)
        {
            byte[] input = format switch
            {
                "binary-stl" => MeshGeometryTestInput.BinaryTriangle(),
                "ascii-stl" => Encoding.UTF8.GetBytes(MeshTestInput.AsciiTriangle),
                "obj" => Encoding.UTF8.GetBytes(MeshTestInput.ObjTriangle),
                "3mf" => MeshTestInput.Package(),
                _ => throw new ArgumentOutOfRangeException(nameof(format))
            };
            string source = Path.Combine(directory, "model.bin");
            string destination = Path.Combine(directory, "preview.png");
            await File.WriteAllBytesAsync(source, input);
            (int code, _, string error) = await CliTests.RunAsync(["-i", source, destination]);
            Assert.True(code == 0, error);
            byte[] encoded = await File.ReadAllBytesAsync(destination);
            Assert.Equal(512u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(encoded.AsSpan(16)));
            Assert.Equal(512u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(encoded.AsSpan(20)));
            Assert.True(new FileInfo(destination).Length > 100);
        }

        [Theory]
        [InlineData("stl")]
        [InlineData("obj")]
        [InlineData("3mf")]
        public async Task ProbeIdentifiesTheModelAndLabelsConfiguredCanvas(string format)
        {
            byte[] input = format switch
            {
                "stl" => MeshGeometryTestInput.BinaryTriangle(),
                "obj" => Encoding.UTF8.GetBytes(MeshTestInput.ObjTriangle),
                "3mf" => MeshTestInput.Package(),
                _ => throw new ArgumentOutOfRangeException(nameof(format))
            };
            string source = Path.Combine(directory, "model.bin");
            await File.WriteAllBytesAsync(source, input);
            (int code, string text, string error) = await CliTests.RunAsync(["-i", source]);
            Assert.True(code == 0, error);
            Assert.Contains($"Model: {format}; preview canvas 512x512 pixels.", text, StringComparison.Ordinal);
            Assert.Single(Directory.GetFiles(directory));
        }

        [Fact]
        public async Task InvalidGeometryPreservesAnExistingApprovedOutput()
        {
            string source = Path.Combine(directory, "model.bin");
            string destination = Path.Combine(directory, "preview.png");
            await File.WriteAllTextAsync(source, "v 0 0 0\nf 1 2 3\n");
            byte[] original = [1, 2, 3, 4];
            await File.WriteAllBytesAsync(destination, original);
            (int code, _, _) = await CliTests.RunAsync(["-i", source, destination, "--overwrite"]);
            Assert.NotEqual(0, code);
            Assert.Equal(original, await File.ReadAllBytesAsync(destination));
        }

        public void Dispose() => Directory.Delete(directory, recursive: true);
    }
}
