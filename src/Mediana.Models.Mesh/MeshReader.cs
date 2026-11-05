// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Models.Mesh
{
    /// <summary>Loads geometry using bounded content recognition, independently of the filename extension.</summary>
    public static class MeshReader
    {
        private const int MaximumInputBytes = 128 * 1024 * 1024;

        public static async Task<TriangleMesh> ReadFileAsync(string path, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            await using FileStream source = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
            return await ReadAsync(source, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Consumes the source from its current position, leaving it open. Use format-specific readers for custom limits.</summary>
        public static async Task<TriangleMesh> ReadAsync(Stream source, CancellationToken cancellationToken = default)
        {
            using MemoryStream input = await BoundedInput.ReadAsync(source, MaximumInputBytes, cancellationToken).ConfigureAwait(false);
            return await ReadAsync(input.GetBuffer().AsMemory(0, checked((int)input.Length)), cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Borrows complete encoded bytes for loading; the returned geometry owns independent storage.</summary>
        public static async Task<TriangleMesh> ReadAsync(ReadOnlyMemory<byte> encoded, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (encoded.Length > MaximumInputBytes) { throw new InvalidDataException("The model exceeds the input byte limit."); }
            MeshFormat format = MeshRecognition.Recognize(encoded.Span[..Math.Min(encoded.Length, MeshRecognition.ProbeBytes)], encoded.Length);
            switch (format)
            {
                case MeshFormat.Stl:
                    return StlReader.Read(encoded, cancellationToken: cancellationToken);
                case MeshFormat.Obj:
                    return await ObjReader.ReadAsync(encoded, cancellationToken: cancellationToken).ConfigureAwait(false);
                case MeshFormat.ThreeMf:
                    ThreeMfDocument document = await ThreeMfReader.ReadAsync(encoded, cancellationToken: cancellationToken).ConfigureAwait(false);
                    return document.CreateBuildMesh(cancellationToken);
                case MeshFormat.Unknown:
                    throw new NotSupportedException("No supported STL, OBJ or 3MF geometry recognizes the input.");
                default:
                    throw new ArgumentOutOfRangeException(nameof(format));
            }
        }
    }
}
