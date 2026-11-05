// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.IO;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Mediana.Models.Mesh
{
    /// <summary>Loads OBJ polygon geometry, normals and smoothing groups without opening material or texture files.</summary>
    public static class ObjReader
    {
        /// <summary>Combines all OBJ objects and groups into one mesh and leaves the source open.</summary>
        public static async Task<TriangleMesh> ReadAsync(Stream source, ObjReaderOptions? options = null, CancellationToken cancellationToken = default)
        {
            options ??= new ObjReaderOptions();
            options.Validate();
            using MemoryStream input = await BoundedInput.ReadAsync(source, options.MaxInputBytes, cancellationToken).ConfigureAwait(false);
            return await ReadAsync(input.GetBuffer().AsMemory(0, checked((int)input.Length)), options, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Reads complete borrowed OBJ bytes without copying the encoded input.</summary>
        public static async Task<TriangleMesh> ReadAsync(ReadOnlyMemory<byte> encoded, ObjReaderOptions? options = null, CancellationToken cancellationToken = default)
        {
            options ??= new();
            options.Validate();
            cancellationToken.ThrowIfCancellationRequested();
            if (encoded.Length > options.MaxInputBytes) { throw new InvalidDataException("The OBJ input exceeds MaxInputBytes."); }
            using ReadOnlyMemoryStream input = new(encoded);
            using StreamReader reader = new(input);
            return await new ObjParser(options).ReadAsync(reader, cancellationToken).ConfigureAwait(false);
        }
    }
}
