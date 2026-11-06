// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
namespace Mediana.Documents.Pdf
{
    internal class PdfTextRun(Func<string> decode,Vector2 position,double size,PdfValue stream,int? mcid,int sequence)
    {
        private string? _text;
        internal string Text => _text ??= decode();
        internal Vector2 Position { get; } = position;
        internal double Size { get; } = size;
        internal PdfValue Stream { get; } = stream;
        internal int? Mcid { get; } = mcid;
        internal int Sequence { get; } = sequence;
    }
}
