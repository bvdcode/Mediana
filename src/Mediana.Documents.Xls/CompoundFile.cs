// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Text;

namespace Mediana.Documents.Xls
{
    internal class CompoundFile : IDisposable
    {
        private const uint Free = 0xFFFFFFFF, End = 0xFFFFFFFE, FatSector = 0xFFFFFFFD, DifatSector = 0xFFFFFFFC;
        private ReadOnlyMemory<byte> _data;
        private readonly int _sectorSize;
        private readonly uint[] _fat;
        private readonly List<CompoundEntry> _entries = [];
        private uint[]? _miniFat;
        private int[]? _miniRoot;
        private bool _disposed;

        internal CompoundFile(ReadOnlyMemory<byte> data, XlsReaderOptions options, CancellationToken token)
        {
            _data = data;
            ReadOnlySpan<byte> header = data.Span;
            if (data.Length < 512 || !header.StartsWith(new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 }))
            { throw new InvalidDataException("Missing CFB compound-file signature."); }
            int version = U16(header, 26), shift = U16(header, 30);
            if (U16(header, 28) != 0xFFFE || U16(header, 32) != 6 || !((version == 3 && shift == 9) || (version == 4 && shift == 12))
                || U32(header, 56) != 4096 || version == 3 && U32(header, 40) != 0)
            { throw new NotSupportedException("Unsupported CFB header or sector geometry."); }
            _sectorSize = 1 << shift;
            if (data.Length % _sectorSize != 0 || data.Length < _sectorSize * 2) { throw new InvalidDataException("Invalid CFB physical length."); }
            int sectors = data.Length / _sectorSize - 1;
            uint fatCount = U32(header, 44), difatCount = U32(header, 72), difat = U32(header, 68);
            if (fatCount == 0 || fatCount > sectors || difatCount > sectors || options.MaximumDirectoryEntries <= 0)
            { throw new InvalidDataException("Invalid CFB allocation-table bounds."); }
            List<int> fatSectors = [];
            HashSet<uint> used = [];
            for (int i = 0; i < 109; i++) { AddFat(U32(header, 76 + i * 4)); }
            List<int> difatSectors = [];
            for (int i = 0; i < difatCount; i++)
            {
                token.ThrowIfCancellationRequested();
                if (difat >= sectors || !used.Add(difat)) { throw new InvalidDataException("Cyclic or overlapping CFB DIFAT."); }
                difatSectors.Add((int)difat);
                ReadOnlySpan<byte> bytes = Sector((int)difat).Span;
                for (int j = 0; j < _sectorSize / 4 - 1; j++) { AddFat(U32(bytes, j * 4)); }
                difat = U32(bytes, _sectorSize - 4);
            }
            if (difat != End || fatSectors.Count != fatCount || (long)fatCount * (_sectorSize / 4) < sectors)
            { throw new InvalidDataException("CFB DIFAT count or capacity mismatch."); }
            _fat = new uint[sectors];
            int position = 0;
            foreach (int id in fatSectors)
            {
                token.ThrowIfCancellationRequested();
                ReadOnlySpan<byte> table = Sector(id).Span;
                for (int i = 0; i < _sectorSize / 4 && position < sectors; i++) { _fat[position++] = U32(table, i * 4); }
            }
            if (fatSectors.Any(id => _fat[id] != FatSector) || difatSectors.Any(id => _fat[id] != DifatSector))
            { throw new InvalidDataException("CFB allocation sectors have incorrect FAT markers."); }
            int[] directories = Chain(U32(header, 48), _fat, null, token);
            if (version == 4 && directories.Length != U32(header, 40)) { throw new InvalidDataException("CFB directory-sector count mismatch."); }
            using (CompoundStream directory = new((long)directories.Length * _sectorSize, _sectorSize, directories, Sector))
            {
                Span<byte> entry = stackalloc byte[128];
                while (directory.Position < directory.Length)
                {
                    token.ThrowIfCancellationRequested();
                    if (_entries.Count >= options.MaximumDirectoryEntries) { throw new InvalidDataException("CFB exceeds its directory-entry limit."); }
                    directory.ReadExactly(entry);
                    byte type = entry[66];
                    if (type is not (0 or 1 or 2 or 5)) { throw new InvalidDataException("Invalid CFB directory object type."); }
                    int nameLength = U16(entry, 64);
                    string name = "";
                    if (type != 0)
                    {
                        if (nameLength is < 2 or > 64 || nameLength % 2 != 0 || U16(entry, nameLength - 2) != 0)
                        { throw new InvalidDataException("Invalid CFB directory name."); }
                        name = new UnicodeEncoding(false, false, true).GetString(entry[..(nameLength - 2)]);
                    }
                    ulong length = BinaryPrimitives.ReadUInt64LittleEndian(entry[120..]);
                    if (type != 0 && (length > (ulong)data.Length || version == 3 && length > uint.MaxValue))
                    { throw new InvalidDataException("CFB stream length exceeds its physical container."); }
                    _entries.Add(new(name, type, U32(entry, 68), U32(entry, 72), U32(entry, 76), U32(entry, 116), type == 0 ? 0 : (long)length));
                }
            }
            if (_entries.Count == 0 || _entries[0].Type != 5) { throw new InvalidDataException("CFB root directory is missing."); }
            uint miniCount = U32(header, 64), miniStart = U32(header, 60);
            if (miniCount > sectors) { throw new InvalidDataException("CFB mini FAT exceeds the physical container."); }
            if (miniCount != 0)
            {
                CompoundEntry root = _entries[0];
                _miniRoot = Chain(root.Start, _fat, (root.Length + _sectorSize - 1) / _sectorSize, token);
                int miniSectors = checked((int)((root.Length + 63) / 64));
                int[] allocation = Chain(miniStart, _fat, miniCount, token);
                if ((long)allocation.Length * (_sectorSize / 4) < miniSectors) { throw new InvalidDataException("CFB mini FAT is too short."); }
                _miniFat = new uint[miniSectors];
                int cursor = 0;
                foreach (int id in allocation)
                {
                    ReadOnlySpan<byte> bytes = Sector(id).Span;
                    for (int i = 0; i < _sectorSize / 4 && cursor < miniSectors; i++) { _miniFat[cursor++] = U32(bytes, i * 4); }
                }
            }
            else if (miniStart != End) { throw new InvalidDataException("Unexpected CFB mini FAT start sector."); }

            void AddFat(uint id)
            {
                if (id == Free) { return; }
                if (id >= sectors || fatSectors.Count >= fatCount || !used.Add(id)) { throw new InvalidDataException("Invalid or repeated CFB FAT sector."); }
                fatSectors.Add((int)id);
            }
        }

        internal Stream OpenRootStream(string name, CancellationToken token)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Stack<uint> pending = new();
            HashSet<uint> visited = [];
            pending.Push(_entries[0].Child);
            CompoundEntry? result = null;
            while (pending.TryPop(out uint id))
            {
                token.ThrowIfCancellationRequested();
                if (id == Free) { continue; }
                if (id >= _entries.Count || !visited.Add(id)) { throw new InvalidDataException("Cyclic or invalid CFB directory tree."); }
                CompoundEntry entry = _entries[(int)id];
                if (entry.Type is not (1 or 2)) { throw new InvalidDataException("Invalid CFB root child."); }
                pending.Push(entry.Left); pending.Push(entry.Right);
                if (entry.Type == 2 && entry.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    if (result is not null) { throw new InvalidDataException("Duplicate CFB workbook streams."); }
                    result = entry;
                }
            }
            if (result is null) { throw new InvalidDataException("CFB root stream is missing: " + name); }
            if (result.Length == 0) { return new CompoundStream(0, _sectorSize, [], Sector); }
            if (result.Length < 4096)
            {
                if (_miniFat is null || _miniRoot is null) { throw new InvalidDataException("CFB mini stream is missing."); }
                int[] chain = Chain(result.Start, _miniFat, (result.Length + 63) / 64, token);
                return new CompoundStream(result.Length, 64, chain, MiniSector);
            }
            return new CompoundStream(result.Length, _sectorSize, Chain(result.Start, _fat, (result.Length + _sectorSize - 1) / _sectorSize, token), Sector);
        }

        private ReadOnlyMemory<byte> Sector(int id)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            long offset = ((long)id + 1) * _sectorSize;
            if (id < 0 || offset > _data.Length - _sectorSize) { throw new InvalidDataException("CFB sector is outside the input."); }
            return _data.Slice((int)offset, _sectorSize);
        }
        private ReadOnlyMemory<byte> MiniSector(int id)
        {
            long offset = (long)id * 64;
            if (id < 0 || offset > _entries[0].Length - 64) { throw new InvalidDataException("CFB mini sector is outside the root stream."); }
            int root = (int)(offset / _sectorSize), within = (int)(offset % _sectorSize);
            return Sector(_miniRoot![root]).Slice(within, 64);
        }
        private static int[] Chain(uint start, uint[] table, long? count, CancellationToken token)
        {
            if (count is < 0 || count > table.Length) { throw new InvalidDataException("CFB chain exceeds its allocation table."); }
            List<int> chain = [];
            HashSet<uint> visited = [];
            for (uint id = start; id != End; id = table[id])
            {
                token.ThrowIfCancellationRequested();
                if (id >= table.Length || !visited.Add(id) || count is long maximum && chain.Count >= maximum)
                { throw new InvalidDataException("Cyclic, excessive or invalid CFB sector chain."); }
                chain.Add((int)id);
            }
            if (count is long expected && chain.Count != expected) { throw new InvalidDataException("CFB stream chain length mismatch."); }
            return chain.ToArray();
        }
        internal static ushort U16(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
        internal static uint U32(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
        public void Dispose() { _disposed = true; _data = default; }
    }
}
