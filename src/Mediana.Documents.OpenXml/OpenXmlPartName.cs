// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents.OpenXml
{
    internal static class OpenXmlPartName
    {
        internal static string Resolve(string sourcePart, string target)
        {
            if (string.IsNullOrWhiteSpace(target) || target.Contains('\\') || target.Contains('?') || target.Contains('#')
                || target.Contains(':') || target.Contains('\0'))
            {
                throw new InvalidDataException("Invalid internal OPC relationship target.");
            }
            string path = target.StartsWith('/') ? target[1..] : sourcePart[..(sourcePart.LastIndexOf('/') + 1)] + target;
            List<string> parts = [];
            foreach (string encoded in path.Split('/'))
            {
                string part = Uri.UnescapeDataString(encoded);
                if (part == ".") { continue; }
                if (part == "..")
                {
                    if (parts.Count == 0) { throw new InvalidDataException("OPC relationship escapes the package root."); }
                    parts.RemoveAt(parts.Count - 1);
                    continue;
                }
                if (part.Length == 0 || part.Contains('/') || part.Contains('\\') || part.Any(char.IsControl))
                {
                    throw new InvalidDataException("Invalid OPC part name.");
                }
                parts.Add(encoded);
            }
            if (parts.Count == 0) { throw new InvalidDataException("An OPC relationship must identify a part."); }
            return string.Join('/', parts);
        }
    }
}
