// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Images.Apk
{
    internal class AndroidResourceTable
    {
        private const int SparseOffsets = 1;
        private const int ShortOffsets = 2;
        private const int ComplexEntry = 1;
        private const int CompactEntry = 8;
        private const int AnyDensity = 0xfffe;
        private const int NoDensity = 0xffff;
        private readonly Dictionary<uint, List<ResourceChunk>> types = [];
        private readonly Dictionary<uint, ResourceValue> values = [];
        private readonly AndroidStringPool strings;
        private readonly float targetDensity;

        public AndroidResourceTable(byte[] bytes, int iconSize, CancellationToken token)
        {
            ResourceChunk table = new(bytes);
            if (table.Type != 2 || table.Length != bytes.Length) { throw ResourceChunk.Invalid(); }
            AndroidStringPool? pool = null;
            foreach (ResourceChunk chunk in table.Children())
            {
                if (chunk.Type == 1) { pool = new(chunk); }
                if (chunk.Type != 0x200) { continue; }
                uint packageId = chunk.UInt32(8);
                uint typeOffset = chunk.HeaderSize >= 288 ? chunk.UInt32(284) : 0;
                foreach (ResourceChunk type in chunk.Children())
                {
                    token.ThrowIfCancellationRequested();
                    if (type.Type != 0x201) { continue; }
                    uint typeId = type.Byte(8) + typeOffset;
                    if (packageId > 255 || typeId is 0 or > 255) { throw ResourceChunk.Invalid(); }
                    uint prefix = (packageId << 8) | typeId;
                    if (!types.TryGetValue(prefix, out List<ResourceChunk>? variants))
                    {
                        variants = [];
                        types.Add(prefix, variants);
                    }
                    variants.Add(type);
                }
            }
            strings = pool ?? throw ResourceChunk.Invalid();
            // Android's baseline launcher bitmap is 48 pixels at 160 dpi.
            targetDensity = iconSize * (160f / 48);
        }

        public ResourceValue Resolve(ResourceValue value)
        {
            for (int depth = 0; value.Type == ResourceValue.Reference; depth++)
            {
                if (depth >= 32) { throw new InvalidDataException("Android resource reference chain is cyclic or too deep."); }
                uint id = value.Data;
                if (!values.TryGetValue(id, out value))
                {
                    value = Lookup(id);
                    values.Add(id, value);
                }
            }
            return value;
        }

        private ResourceValue Lookup(uint id)
        {
            switch (id)
            {
                case 0x0106000b: return new(0x1c, 0xffffffff, null); // android:color/white
                case 0x0106000c: return new(0x1c, 0xff000000, null); // android:color/black
                case 0x0106000d: return new(0x1c, 0, null);          // android:color/transparent
            }
            if (!types.TryGetValue(id >> 16, out List<ResourceChunk>? variants))
            {
                throw new InvalidDataException($"Missing Android resource 0x{id:x8}.");
            }
            (int AnyDensity, double Density, int Version)? best = null;
            ResourceChunk selected = default;
            int entry = 0;
            foreach (ResourceChunk type in variants)
            {
                int configSize = type.Offset(20);
                if (configSize < 28 || configSize > type.HeaderSize - 20) { throw ResourceChunk.Invalid(); }
                // With no device configuration, use the unqualified locale, theme and screen.
                ReadOnlySpan<byte> config = type.Slice(20, configSize);
                if (config[4..14].ContainsAnyExcept((byte)0) || config[16..24].ContainsAnyExcept((byte)0)
                    || config[28..].ContainsAnyExcept((byte)0)) { continue; }
                int offset = Entry(type, (ushort)id);
                if (offset < 0) { continue; }
                int density = type.UInt16(34);
                int dpi = density is 0 or NoDensity ? 160 : density;
                (int AnyDensity, double Density, int Version) score =
                    (density == AnyDensity ? 1 : 0, -Math.Abs(Math.Log2(dpi / targetDensity)), type.UInt16(44));
                if (best.HasValue && score.CompareTo(best.Value) <= 0) { continue; }
                best = score;
                selected = type;
                entry = offset;
            }
            if (best is null) { throw new InvalidDataException($"Missing default variant for Android resource 0x{id:x8}."); }
            int flags = selected.UInt16(entry + 2);
            if ((flags & ComplexEntry) != 0) { throw new NotSupportedException("Complex Android resource bags cannot be drawn as icons."); }
            if ((flags & CompactEntry) != 0) { return ResourceValue.Create((byte)(flags >> 8), selected.UInt32(entry + 4), strings); }
            return ResourceValue.Read(selected, checked(entry + selected.UInt16(entry)), strings);
        }

        private static int Entry(ResourceChunk type, int index)
        {
            uint count = type.UInt32(12);
            int start = type.Offset(16);
            int offsets = type.HeaderSize;
            int flags = type.Byte(9);
            int stride = flags switch { 0 or SparseOffsets => 4, ShortOffsets => 2, _ => throw ResourceChunk.Invalid() };
            if (start < offsets || count > (start - offsets) / stride) { throw ResourceChunk.Invalid(); }
            uint relative;
            if (flags == SparseOffsets)
            {
                int low = 0, high = (int)count;
                while (low < high)
                {
                    int middle = low + (high - low) / 2;
                    if (type.UInt16(offsets + middle * 4) < index) { low = middle + 1; }
                    else { high = middle; }
                }
                if (low == count || type.UInt16(offsets + low * 4) != index) { return -1; }
                relative = (uint)type.UInt16(offsets + low * 4 + 2) * 4;
            }
            else
            {
                if (index >= count) { return -1; }
                if (stride == 2)
                {
                    int shortOffset = type.UInt16(offsets + index * 2);
                    if (shortOffset == ushort.MaxValue) { return -1; }
                    relative = (uint)shortOffset * 4;
                }
                else
                {
                    relative = type.UInt32(offsets + index * 4);
                    if (relative == uint.MaxValue) { return -1; }
                }
            }
            if (relative > type.Length - start) { throw ResourceChunk.Invalid(); }
            return start + (int)relative;
        }
    }
}
