// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;

namespace Mediana.Documents.Xls
{
    internal class BiffRecordReader(Stream source, int maximumRecords, CancellationToken token) : IDisposable
    {
        private readonly byte[] _buffer = new byte[8224];
        private int _records;
        internal BiffRecordType Type { get; private set; }
        internal ReadOnlyMemory<byte> Data { get; private set; }
        internal CancellationToken Token => token;
        internal bool MoveNext()
        {
            token.ThrowIfCancellationRequested();
            Span<byte> header = stackalloc byte[4];
            int first = source.Read(header);
            if (first == 0) { return false; }
            source.ReadExactly(header[first..]);
            if (++_records > maximumRecords) { throw new InvalidDataException("XLS exceeds MaximumRecords."); }
            Type = (BiffRecordType)BinaryPrimitives.ReadUInt16LittleEndian(header);
            int count = BinaryPrimitives.ReadUInt16LittleEndian(header[2..]);
            if (count > _buffer.Length) { throw new InvalidDataException("A BIFF8 record exceeds 8224 bytes."); }
            source.ReadExactly(_buffer.AsSpan(0, count));
            Data = _buffer.AsMemory(0, count);
            return true;
        }
        internal void Require(int length)
        {
            if (Data.Length < length) { throw new InvalidDataException("Truncated BIFF record " + Type + "."); }
        }
        internal void Bof(ushort substream)
        {
            if (!MoveNext() || Type != BiffRecordType.Bof) { throw new InvalidDataException("Missing BIFF BOF record."); }
            Require(16);
            if (CompoundFile.U16(Data.Span, 0) != 0x0600) { throw new NotSupportedException("The XLS reader requires BIFF8 (Excel 97-2003)."); }
            if (CompoundFile.U16(Data.Span, 2) != substream) { throw new InvalidDataException("Unexpected BIFF substream type."); }
        }
        public void Dispose() => source.Dispose();
    }
}
