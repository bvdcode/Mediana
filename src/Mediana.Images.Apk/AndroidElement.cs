// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Images.Apk
{
    internal class AndroidElement(string name)
    {
        private const string AndroidNamespace = "http://schemas.android.com/apk/res/android";
        public string Name { get; } = name;
        public Dictionary<string, ResourceValue> Attributes { get; } = new(StringComparer.Ordinal);
        public List<AndroidElement> Children { get; } = [];

        public AndroidElement? Child(string name)
        {
            AndroidElement? result = null;
            foreach (AndroidElement child in Children)
            {
                if (child.Name != name) { continue; }
                if (result is not null) { throw new InvalidDataException($"Duplicate Android element '{name}'."); }
                result = child;
            }
            return result;
        }

        public static AndroidElement Parse(byte[] bytes, CancellationToken token)
        {
            ResourceChunk xml = new(bytes);
            if (xml.Type != 3 || xml.Length != bytes.Length) { throw ResourceChunk.Invalid(); }
            AndroidStringPool? pool = null;
            Stack<AndroidElement> parents = [];
            AndroidElement? root = null;
            foreach (ResourceChunk chunk in xml.Children())
            {
                token.ThrowIfCancellationRequested();
                if (chunk.Type == 1) { pool = new(chunk); }
                if (chunk.Type is not (0x102 or 0x103)) { continue; }
                if (pool is null || chunk.HeaderSize < 16) { throw ResourceChunk.Invalid(); }
                int extension = chunk.HeaderSize;
                string name = pool.Get(chunk.UInt32(extension + 4));
                if (chunk.Type == 0x103)
                {
                    if (!parents.TryPop(out AndroidElement? last) || last.Name != name) { throw ResourceChunk.Invalid(); }
                    continue;
                }
                if (parents.Count >= 64) { throw ResourceChunk.Invalid(); }
                AndroidElement element = new(name);
                if (parents.TryPeek(out AndroidElement? parent)) { parent.Children.Add(element); }
                else if (root is null) { root = element; }
                else { throw ResourceChunk.Invalid(); }
                parents.Push(element);
                int first = extension + chunk.UInt16(extension + 8);
                int stride = chunk.UInt16(extension + 10);
                int count = chunk.UInt16(extension + 12);
                if (first < extension + 20 || first > chunk.Length || stride < 20 || count > (chunk.Length - first) / stride) { throw ResourceChunk.Invalid(); }
                for (int i = 0; i < count; i++)
                {
                    int attribute = first + i * stride;
                    uint ns = chunk.UInt32(attribute);
                    if (ns == uint.MaxValue || pool.Get(ns) != AndroidNamespace) { continue; }
                    string key = pool.Get(chunk.UInt32(attribute + 4));
                    if (!element.Attributes.TryAdd(key, ResourceValue.Read(chunk, attribute + 12, pool))) { throw ResourceChunk.Invalid(); }
                }
            }
            return parents.Count == 0 && root is not null ? root : throw ResourceChunk.Invalid();
        }
    }
}
