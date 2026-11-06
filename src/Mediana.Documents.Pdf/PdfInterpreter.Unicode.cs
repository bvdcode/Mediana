// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Text;
namespace Mediana.Documents.Pdf
{
    internal partial class PdfInterpreter
    {
        private Dictionary<int,int> ReadToUnicode(PdfValue reference,bool composite)
        {
            Dictionary<int,string> strings = PdfUnicodeMap.Read(storage,reference,composite,token,ChargeOperations,message => _warnings.Add(message));
            Dictionary<int,int> result = new(strings.Count);
            foreach ((int code,string value) in strings)
            {
                Rune scalar = Rune.GetRuneAt(value,0);
                result.Add(code,scalar.Utf16SequenceLength == value.Length ? scalar.Value : -1);
            }
            return result;
        }
    }
}
