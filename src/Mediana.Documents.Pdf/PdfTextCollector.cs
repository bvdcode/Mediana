// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
namespace Mediana.Documents.Pdf
{
    internal partial class PdfTextCollector(PdfStorage storage, PdfOptionalContent optionalContent, long maximumCharacters, Action<string>? warning, CancellationToken token)
    {
        private readonly Dictionary<PdfValue, PdfTextFont> _fonts = [];
        private readonly Dictionary<PdfValue, PdfTextFont> _metrics = [];
        private readonly List<PdfTextRun> _runs = [];
        private readonly List<PdfTextMark> _marks = [];
        private readonly HashSet<PdfValue> _active = [];
        private int _operations;
        private long _characters;
        internal IReadOnlyList<PdfTextRun> Read(PdfPageSource page)
        {
            PdfTextState state = new() { Transform = page.Transform };
            Walk(PdfPageContents.Read(storage, page.Dictionary.Get("Contents"), token), page.Resources, page.Dictionary, state, 0);
            return _runs.AsReadOnly();
        }
        private void Charge(int count)
        {
            token.ThrowIfCancellationRequested();
            if (count < 0 || count > storage.Budget.Options.MaximumOperations - _operations) { throw new InvalidDataException("PDF text exceeds its operation limit."); }
            _operations += count;
        }
        private PdfValue Resolve(PdfValue value) => storage.Resolve(value, token);
        private PdfValue Get(PdfValue value, string name) => Resolve(value.Get(name));
        private void Add(string text, Vector2 point, double size, PdfValue stream, int? mcid)
        {
            Charge(1);
            if (text.Length > maximumCharacters - _characters) { throw new InvalidDataException("PDF page text exceeds its character limit."); }
            _characters += text.Length;
            if (text.Length != 0) { _runs.Add(new(() => text, point, size, stream, mcid, _runs.Count)); }
        }
        private void Walk(ReadOnlyMemory<byte> bytes, PdfValue resources, PdfValue stream, PdfTextState initial, int depth)
        {
            if (depth >= storage.Budget.Options.MaximumDepth || !_active.Add(stream)) { throw new InvalidDataException("Cyclic or excessive PDF text form nesting."); }
            PdfSyntax syntax = new(bytes, storage.Budget, token);
            PdfTextState state = initial with { };
            Stack<PdfTextState> stack = new();
            Matrix3x2 text = Matrix3x2.Identity, line = Matrix3x2.Identity;
            List<PdfValue> args = [];
            bool inText = false;
            int markFloor = _marks.Count;
            try
            {
                while (true)
                {
                    syntax.Skip(); if (syntax.Position == bytes.Length) { break; }
                    PdfValue value = syntax.Read();
                    if (value.Kind != PdfValueKind.Keyword)
                    { if (args.Count >= 4096) { throw new InvalidDataException("Too many PDF text operands."); } args.Add(value); continue; }
                    Charge(1);
                    string op = value.Text;
                    switch (op)
                    {
                        case "q": Require(0); stack.Push(state with { }); if (stack.Count > storage.Budget.Options.MaximumDepth) { throw new InvalidDataException("Excessive PDF graphics nesting."); } break;
                        case "Q": Require(0); if (!stack.TryPop(out state!)) { throw new InvalidDataException("Unbalanced PDF graphics state."); } break;
                        case "cm": Require(6); state.Transform = Matrix(args) * state.Transform; break;
                        case "BT": Require(0); if (inText) { throw new InvalidDataException("Nested PDF text object."); } inText = true; text = line = Matrix3x2.Identity; break;
                        case "ET": Require(0); Text(); inText = false; break;
                        case "Tf": Require(2); Text(); state.Font = Get(Get(resources, "Font"), args[0].Name()); state.Size = args[1].Numeric(); break;
                        case "Tc": Require(1); state.CharacterSpace = args[0].Numeric(); break;
                        case "Tw": Require(1); state.WordSpace = args[0].Numeric(); break;
                        case "Tz": Require(1); state.Horizontal = args[0].Numeric() / 100; break;
                        case "TL": Require(1); state.Leading = args[0].Numeric(); break;
                        case "Ts": Require(1); state.Rise = args[0].Numeric(); break;
                        case "Tr": Require(1); state.Mode = args[0].Integer(); if (state.Mode is < 0 or > 7) { throw new InvalidDataException("Invalid PDF text rendering mode."); } break;
                        case "Tm": Require(6); Text(); text = line = Matrix(args); break;
                        case "Td": Require(2); Move(args[0].Numeric(), args[1].Numeric()); break;
                        case "TD": Require(2); state.Leading = -args[1].Numeric(); Move(args[0].Numeric(), args[1].Numeric()); break;
                        case "T*": Require(0); Move(0, -state.Leading); break;
                        case "Tj": Require(1); Show(args[0]); break;
                        case "TJ":
                            Require(1); Text();
                            if (args[0].Kind != PdfValueKind.Array) { throw new InvalidDataException("Expected PDF TJ array."); }
                            foreach (PdfValue item in args[0].Items)
                            {
                                if (item.Kind == PdfValueKind.String) { Show(item); }
                                else
                                {
                                    double distance = -item.Numeric() * state.Size * state.Horizontal / 1000;
                                    if (distance > Math.Abs(state.Size) * .2 && _marks.All(mark => mark.ActualText is null) && Visible())
                                    { Add(" ", Position(), state.Size, stream, Mcid()); }
                                    text = Matrix3x2.CreateTranslation((float)distance, 0) * text;
                                }
                            }
                            break;
                        case "'": Require(1); Move(0, -state.Leading); Show(args[0]); break;
                        case "\"": Require(3); state.WordSpace = args[0].Numeric(); state.CharacterSpace = args[1].Numeric(); Move(0, -state.Leading); Show(args[2]); break;
                        case "BMC":
                        case "BDC":
                            Require(op == "BMC" ? 1 : 2);
                            string tag = args[0].Name();
                            PdfValue properties = op == "BDC" ? args[1].Kind == PdfValueKind.Name ? Get(Get(resources, "Properties"), args[1].Text) : Resolve(args[1]) : PdfValue.Null;
                            int? mcid = properties.Get("MCID").Kind == PdfValueKind.Null ? null : Get(properties, "MCID").Integer();
                            string? actual = properties.Get("ActualText").Kind == PdfValueKind.Null ? null : PdfTextStrings.Decode(Get(properties, "ActualText"));
                            bool visible = tag != "OC" || optionalContent.IsVisible(properties, token, ref _operations);
                            _marks.Add(new(mcid, actual, visible, tag == "Artifact", stream, Position()));
                            if (_marks.Count > storage.Budget.Options.MaximumDepth) { throw new InvalidDataException("Excessive PDF marked-content nesting."); }
                            break;
                        case "EMC":
                            Require(0); if (_marks.Count <= markFloor) { throw new InvalidDataException("Unbalanced PDF marked content."); }
                            PdfTextMark closing = _marks[^1]; _marks.RemoveAt(_marks.Count - 1);
                            if (closing.ActualText is string replacement && closing.Visible && !closing.Artifact && Visible() && _marks.All(mark => mark.ActualText is null))
                            { Add(replacement, closing.Position, state.Size, closing.Stream, closing.Mcid ?? Mcid()); }
                            break;
                        case "Do":
                            Require(1);
                            PdfValue reference = Get(resources, "XObject").Get(args[0].Name());
                            PdfObject xobject = storage.Object(reference, token);
                            if (Get(xobject.Value, "Subtype").Text == "Form" && Visible())
                            {
                                PdfValue oc = xobject.Value.Get("OC");
                                if (oc.Kind != PdfValueKind.Null && !optionalContent.IsVisible(oc, token, ref _operations)) { break; }
                                PdfValue matrix = Get(xobject.Value, "Matrix");
                                PdfTextState formState = state with { Transform = (matrix.Kind == PdfValueKind.Null ? Matrix3x2.Identity : Matrix(matrix.Items)) * state.Transform };
                                PdfValue nested = Get(xobject.Value, "Resources");
                                Walk(storage.Decode(xobject, token), nested.Kind == PdfValueKind.Null ? resources : nested, xobject.Value, formState, depth + 1);
                            }
                            break;
                        case "gs":
                            Require(1); PdfValue gs = Get(Get(resources, "ExtGState"), args[0].Name()); PdfValue font = Get(gs, "Font");
                            if (font.Kind != PdfValueKind.Null)
                            { if (font.Items.Length != 2) { throw new InvalidDataException("Invalid PDF graphics-state font."); } state.Font = Resolve(font.Items[0]); state.Size = font.Items[1].Numeric(); }
                            break;
                        case "BI": throw new NotSupportedException("PDF text extraction from inline-image content streams is not supported.");
                        case "m":
                        case "l":
                        case "c":
                        case "v":
                        case "y":
                        case "h":
                        case "re":
                        case "S":
                        case "s":
                        case "f":
                        case "F":
                        case "f*":
                        case "B":
                        case "B*":
                        case "b":
                        case "b*":
                        case "n":
                        case "W":
                        case "W*":
                        case "w":
                        case "J":
                        case "j":
                        case "M":
                        case "d":
                        case "ri":
                        case "i":
                        case "CS":
                        case "cs":
                        case "SC":
                        case "SCN":
                        case "sc":
                        case "scn":
                        case "G":
                        case "g":
                        case "RG":
                        case "rg":
                        case "K":
                        case "k":
                        case "sh":
                        case "MP":
                        case "DP":
                        case "BX":
                        case "EX": break;
                        default: throw new NotSupportedException("Unknown PDF content operator: " + op);
                    }
                    args.Clear();
                }
                if (inText || stack.Count != 0 || _marks.Count != markFloor || args.Count != 0) { throw new InvalidDataException("Unbalanced PDF text content stream."); }
            }
            finally
            {
                _active.Remove(stream);
                if (_marks.Count > markFloor) { _marks.RemoveRange(markFloor, _marks.Count - markFloor); }
            }
            void Require(int count) { if (args.Count != count) { throw new InvalidDataException("Invalid PDF text operand count."); } }
            void Text() { if (!inText) { throw new InvalidDataException("PDF text operator outside BT/ET."); } }
            void Move(double x, double y) { Text(); text = line = Matrix3x2.CreateTranslation((float)x, (float)y) * line; }
            Vector2 Position() => Vector2.Transform(new(0, (float)state.Rise), text * state.Transform);
            bool Visible() => _marks.All(mark => mark.Visible && !mark.Artifact);
            int? Mcid() => _marks.LastOrDefault(mark => mark.Mcid is not null)?.Mcid;
            void Show(PdfValue source)
            {
                Text(); if (source.Kind != PdfValueKind.String) { throw new InvalidDataException("Expected PDF text bytes."); }
                if (state.Font.Kind == PdfValueKind.Null) { throw new InvalidDataException("PDF text has no selected font."); }
                if (!_metrics.TryGetValue(state.Font, out PdfTextFont? metrics))
                { metrics = new(storage, state.Font, token, Charge, false); _metrics.Add(state.Font, metrics); }
                (_, double advance) = metrics.Decode(source.Bytes, state.Size, state.CharacterSpace, state.WordSpace, state.Horizontal, false);
                if (Visible() && !_marks.Any(mark => mark.ActualText is not null))
                {
                    PdfValue font = state.Font;
                    PdfTextMark? mark = _marks.LastOrDefault(item => item.Mcid is not null);
                    _runs.Add(new(() => Decode(font, source.Bytes), Position(), Math.Abs(state.Size), mark?.Stream ?? stream, mark?.Mcid, _runs.Count));
                }
                text = Matrix3x2.CreateTranslation((float)advance, 0) * text;
            }
        }
        private string Decode(PdfValue font, byte[] bytes)
        {
            if (!_fonts.TryGetValue(font, out PdfTextFont? decoder))
            { decoder = new(storage, font, token, Charge, warning: warning); _fonts.Add(font, decoder); }
            (string text, _) = decoder.Decode(bytes, 0, 0, 0, 1);
            if (text.Length > maximumCharacters - _characters) { throw new InvalidDataException("PDF page text exceeds its character limit."); }
            _characters += text.Length;
            return text;
        }
        private static Matrix3x2 Matrix(IReadOnlyList<PdfValue> values)
        {
            if (values.Count != 6) { throw new InvalidDataException("Invalid PDF text transform."); }
            float N(int i) { double n = values[i].Numeric(); if (n is < -float.MaxValue or > float.MaxValue) { throw new InvalidDataException("PDF transform overflows."); } return (float)n; }
            return new(N(0), N(1), N(2), N(3), N(4), N(5));
        }
    }
}
