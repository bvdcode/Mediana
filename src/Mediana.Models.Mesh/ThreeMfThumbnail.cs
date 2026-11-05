// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;
using System.Numerics;

namespace Mediana.Models.Mesh
{
    /// <summary>An original package-level thumbnail; it represents the package, not a selected object.</summary>
    public class ThreeMfThumbnail
    {
        /// <summary>Gets the package-relative image path.</summary>
        public string PartPath { get; }
        /// <summary>Gets image/png or image/jpeg.</summary>
        public string ContentType { get; }
        /// <summary>Gets the original encoded image bytes.</summary>
        public System.ReadOnlyMemory<byte> Data { get; }

        internal ThreeMfThumbnail(string path, string contentType, byte[] data)
        {
            PartPath = path;
            ContentType = contentType;
            Data = data;
        }
    }
}
