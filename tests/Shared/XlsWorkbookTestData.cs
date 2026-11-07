// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Text;
namespace Mediana.Tests
{
    internal static class XlsWorkbookTestData
    {
        internal static byte[] Create(bool mini = true, bool version4 = false, bool continued = false)
        {
            using MemoryStream workbook = new();
            using BinaryWriter writer = new(workbook, Encoding.UTF8, true);
            Record(0x0809, Bof(5)); Record(0x0022, new byte[2]);
            Record(0x00e0, new byte[20]); byte[] date = new byte[20]; U16(date, 2, 14); Record(0x00e0, date);
            if (continued)
            {
                byte[] first = new byte[14]; U32(first, 0, 1); U32(first, 4, 1); U16(first, 8, 6); first[10] = 0; Encoding.Latin1.GetBytes("abc").CopyTo(first, 11);
                Record(0x00fc, first); Record(0x003c, new byte[] { 1, 0x16, 0x04, 0x15, 0x04, 0x00, 0x4e });
            }
            else
            {
                byte[] text = Text("Привет 世界 😀"); byte[] sst = new byte[8 + text.Length]; U32(sst, 0, 1); U32(sst, 4, 1); text.CopyTo(sst, 8); Record(0x00fc, sst);
            }
            long bound = workbook.Position; byte[] sheet = new byte[13]; sheet[6] = 5; sheet[7] = 0; Encoding.Latin1.GetBytes("Sheet").CopyTo(sheet, 8); Record(0x0085, sheet); Record(0x000a, []);
            long offset = workbook.Position;
            byte[] label = Cell(0, 0, 0, 4); U32(label, 6, 0); Record(0x00fd, label);
            // Insert the worksheet BOF immediately before its first cell.
            byte[] globalsAndCell = workbook.ToArray(); workbook.SetLength(offset); Record(0x0809, Bof(0x10)); writer.Write(globalsAndCell.AsSpan((int)offset));
            byte[] boolean = Cell(0, 1, 0, 2); boolean[6] = 1; Record(0x0205, boolean);
            byte[] error = Cell(0, 2, 0, 2); error[6] = 23; error[7] = 1; Record(0x0205, error);
            byte[] number = Cell(0, 3, 1, 8); BinaryPrimitives.WriteDoubleLittleEndian(number.AsSpan(6), 60.5); Record(0x0203, number);
            byte[] formula = Cell(0, 4, 0, 19); BinaryPrimitives.WriteDoubleLittleEndian(formula.AsSpan(6), 4); U16(formula, 20, 3); formula[22] = 0x1e; formula[23] = 4; Record(0x0006, formula);
            byte[] stringFormula = Cell(0, 5, 0, 16); U16(stringFormula, 12, 65535); Record(0x0006, stringFormula); Record(0x0207, Text("cached 😀"));
            byte[] rk = Cell(2, 0, 0, 4); U32(rk, 6, (12345u << 2) | 3); Record(0x027e, rk); Record(0x000a, []);
            byte[] data = workbook.ToArray(); U32(data, (int)bound + 4, (uint)offset);
            return Compound(data, mini, version4);
            void Record(int type, byte[] data) { writer.Write((ushort)type); writer.Write((ushort)data.Length); writer.Write(data); }
        }
        internal static byte[] Compound(byte[] stream, bool mini, bool version4 = false)
        {
            if (version4) { mini = false; }
            int sector = version4 ? 4096 : 512;
            int streamLength = mini ? stream.Length : Math.Max(4096, stream.Length);
            int rootLength = mini ? (streamLength + 63) / 64 * 64 : 0;
            int payloadSectors = ((mini ? rootLength : streamLength) + sector - 1) / sector;
            int sectors = 2 + payloadSectors + (mini ? 1 : 0);
            byte[] data = new byte[(sectors + 1) * sector];
            new byte[] { 0xd0, 0xcf, 0x11, 0xe0, 0xa1, 0xb1, 0x1a, 0xe1 }.CopyTo(data, 0);
            U16(data, 24, 0x3e); U16(data, 26, version4 ? 4 : 3); U16(data, 28, 0xfffe); U16(data, 30, version4 ? 12 : 9); U16(data, 32, 6);
            U32(data, 40, version4 ? 1u : 0u); U32(data, 44, 1); U32(data, 48, 1); U32(data, 56, 4096);
            U32(data, 60, mini ? (uint)(2 + payloadSectors) : 0xfffffffe); U32(data, 64, mini ? 1u : 0u); U32(data, 68, 0xfffffffe);
            for (int i = 0; i < 109; i++) { U32(data, 76 + i * 4, 0xffffffff); }
            U32(data, 76, 0);
            for (int i = 0; i < sector / 4; i++) { U32(data, sector + i * 4, 0xffffffff); }
            U32(data, sector, 0xfffffffd); U32(data, sector + 4, 0xfffffffe);
            for (int i = 0; i < payloadSectors; i++) { U32(data, sector + (2 + i) * 4, i + 1 == payloadSectors ? 0xfffffffe : (uint)(3 + i)); }
            Directory(0, "Root Entry", 5, mini ? 2u : 0xfffffffe, (ulong)rootLength, 1);
            Directory(1, "Workbook", 2, mini ? 0u : 2u, (ulong)streamLength, 0xffffffff);
            stream.CopyTo(data, 3 * sector);
            if (mini)
            {
                int miniSector = 2 + payloadSectors; U32(data, sector + miniSector * 4, 0xfffffffe);
                int start = (miniSector + 1) * sector;
                for (int i = 0; i < sector / 4; i++) { U32(data, start + i * 4, 0xffffffff); }
                for (int i = 0; i < rootLength / 64; i++) { U32(data, start + i * 4, i + 1 == rootLength / 64 ? 0xfffffffe : (uint)(i + 1)); }
            }
            return data;
            void Directory(int index, string name, int type, uint start, ulong length, uint child)
            {
                int at = 2 * sector + index * 128; byte[] encoded = Encoding.Unicode.GetBytes(name + '\0'); encoded.CopyTo(data, at);
                U16(data, at + 64, encoded.Length); data[at + 66] = (byte)type; data[at + 67] = 1;
                U32(data, at + 68, 0xffffffff); U32(data, at + 72, 0xffffffff); U32(data, at + 76, child); U32(data, at + 116, start);
                BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(at + 120), length);
            }
        }
        private static byte[] Cell(int row, int column, int style, int payload) { byte[] data = new byte[6 + payload]; U16(data, 0, row); U16(data, 2, column); U16(data, 4, style); return data; }
        private static byte[] Bof(int kind) { byte[] data = new byte[16]; U16(data, 0, 0x600); U16(data, 2, kind); return data; }
        private static byte[] Text(string text) { byte[] chars = Encoding.Unicode.GetBytes(text); byte[] data = new byte[3 + chars.Length]; U16(data, 0, text.Length); data[2] = 1; chars.CopyTo(data, 3); return data; }
        internal static void U16(byte[] data, int offset, int value) => BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset), (ushort)value);
        internal static void U32(byte[] data, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset), value);
    }
}
