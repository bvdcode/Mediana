// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents.Xls
{
    internal enum BiffRecordType : ushort
    {
        Formula = 0x0006, Eof = 0x000A, Date1904 = 0x0022, FilePass = 0x002F, Continue = 0x003C,
        BoundSheet = 0x0085, MulRk = 0x00BD, MulBlank = 0x00BE, Xf = 0x00E0,
        Sst = 0x00FC, LabelSst = 0x00FD, Blank = 0x0201, Number = 0x0203, Label = 0x0204,
        BoolErr = 0x0205, String = 0x0207, Rk = 0x027E, Format = 0x041E, Bof = 0x0809
    }
}
