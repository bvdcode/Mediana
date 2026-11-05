// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Mediana.Models.Mesh
{
    internal static class MeshRecognition
    {
        internal const int ProbeBytes = 4096;

        internal static MeshFormat Recognize(ReadOnlySpan<byte> prefix, long? encodedLength = null)
        {
            if (prefix.StartsWith("PK\x03\x04"u8)) { return MeshFormat.ThreeMf; }
            if (prefix.Length >= 134 && encodedLength is long length)
            {
                uint triangles = BinaryPrimitives.ReadUInt32LittleEndian(prefix[80..]);
                if (triangles > 0 && 84UL + (50UL * triangles) == (ulong)length && FiniteTriangle(prefix[96..134]))
                {
                    return MeshFormat.Stl;
                }
            }
            if (prefix.Contains((byte)0)) { return MeshFormat.Unknown; }
            if (prefix.StartsWith<byte>([0xef, 0xbb, 0xbf])) { prefix = prefix[3..]; }
            using StringReader reader = new(Encoding.UTF8.GetString(prefix));
            while (reader.ReadLine() is string line)
            {
                line = line.Trim();
                if (line.Length == 0 || line.StartsWith('#')) { continue; }
                string[] fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (fields[0].Equals("solid", StringComparison.OrdinalIgnoreCase)) { return MeshFormat.Stl; }
                if (fields[0] is "o" or "g" or "mtllib" or "usemtl" or "s" && fields.Length >= 2) { return MeshFormat.Obj; }
                if (fields[0] is "v" or "vn" && fields.Length >= 4
                    && Finite(fields[1]) && Finite(fields[2]) && Finite(fields[3])) { return MeshFormat.Obj; }
                if (fields[0] == "vt" && fields.Length >= 2 && Finite(fields[1])) { return MeshFormat.Obj; }
                return MeshFormat.Unknown;
            }
            return MeshFormat.Unknown;
        }

        private static bool Finite(string value) => float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result) && float.IsFinite(result);

        private static bool FiniteTriangle(ReadOnlySpan<byte> data)
        {
            for (int offset = 0; offset < 36; offset += 4)
            {
                if (!float.IsFinite(BinaryPrimitives.ReadSingleLittleEndian(data[offset..]))) { return false; }
            }
            return true;
        }
    }
}
