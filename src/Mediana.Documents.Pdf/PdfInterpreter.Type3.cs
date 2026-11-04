// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Documents.Pdf
{
    internal partial class PdfInterpreter
    {
        private Dictionary<int, double> SimpleWidths(PdfValue dictionary)
        {
            int first = Get(dictionary, "FirstChar").Integer();
            PdfValue[] values = Get(dictionary, "Widths").Items;
            if (first is < 0 or > 255 || values.Length > 256 - first) { throw new InvalidDataException("Invalid PDF font widths."); }
            Dictionary<int, double> widths = [];
            for (int i = 0; i < values.Length; i++) { widths[first + i] = Resolve(values[i]).Numeric(); }
            return widths;
        }

        private PdfType3Font? CreateType3Font(PdfValue dictionary)
        {
            PdfValue encoding = Get(dictionary, "Encoding");
            if (encoding.Kind != PdfValueKind.Dictionary || Get(encoding, "BaseEncoding").Kind != PdfValueKind.Null) { return null; }
            Dictionary<int, double> widths = SimpleWidths(dictionary);
            int first = Get(dictionary, "FirstChar").Integer(), last = Get(dictionary, "LastChar").Integer();
            if (last < first || last > 255 || widths.Count != last - first + 1)
            {
                throw new InvalidDataException("Invalid Type 3 character range.");
            }

            PdfValue procedures = Get(dictionary, "CharProcs");
            if (procedures.Kind != PdfValueKind.Dictionary) { throw new InvalidDataException("A Type 3 font requires CharProcs."); }
            Dictionary<int, PdfValue> glyphs = [];
            int current = -1;
            foreach (PdfValue raw in Get(encoding, "Differences").Items)
            {
                PdfValue value = Resolve(raw);
                if (value.Kind == PdfValueKind.Number) { current = value.Integer(); continue; }
                if ((uint)current > 255) { throw new InvalidDataException("Invalid Type 3 encoding Differences."); }
                PdfValue procedure = procedures.Get(value.Name());
                if (procedure.Kind != PdfValueKind.Null) { glyphs[current] = procedure; }
                current++;
            }

            return new(Matrix(Get(dictionary, "FontMatrix")), Get(dictionary, "Resources"), glyphs, widths);
        }

        private void ShowType3(PdfType3Font font, int code, int depth)
        {
            if (!font.Glyphs.TryGetValue(code, out PdfValue? reference)) { return; }
            Matrix3x2 scale = Matrix3x2.CreateScale(_state.FontSize * _state.HorizontalScale, _state.FontSize);
            Matrix3x2 transform = font.Matrix * scale * Matrix3x2.CreateTranslation(0, _state.Rise) * _text * _state.Transform;
            PdfValue resources = font.Resources.Kind == PdfValueKind.Null ? _pageResources : font.Resources;
            ExecuteNested(storage.Object(reference, token), resources, transform, depth + 1, glyph: true);
        }
    }
}
