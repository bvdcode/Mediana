// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using Mediana.Drawing;
namespace Mediana.Documents.Pdf
{
    internal partial class PdfInterpreter(PdfStorage storage, CancellationToken token)
    {
        private readonly List<IPdfDrawCommand> _commands = [];
        private readonly HashSet<string> _unsupported = new(StringComparer.Ordinal);
        private readonly List<PdfImage> _images = [];
        private readonly HashSet<PdfObject> _forms = [];
        private readonly Dictionary<PdfObject, PdfImage> _decodedImages = [];
        private PdfGraphicsState _state = new();
        private VectorPath _path = new();
        private Vector2 _current;
        private Vector2 _start;
        private bool _hasPoint;
        private FillRule? _pendingClip;
        private int _operations;
        private long _imageBytes;
        private Matrix3x2 _text = Matrix3x2.Identity;
        private Matrix3x2 _textLine = Matrix3x2.Identity;
        private bool _inText;
        private VectorPath _textClip = new();
        private bool _hasTextClip;
        internal PdfPage Interpret(PdfPageSource page)
        {
            _state = _state with { Transform = page.Transform };
            if (page.Dictionary.Get("Annots").Kind != PdfValueKind.Null) { _unsupported.Add("Annotation appearance streams"); }
            if (page.Dictionary.Get("Group").Kind != PdfValueKind.Null) { _unsupported.Add("Page transparency groups"); }
            PdfValue contents = Resolve(page.Dictionary.Get("Contents"));
            if (contents.Kind != PdfValueKind.Null)
            {
                PdfValue source = page.Dictionary.Get("Contents");
                PdfValue[] streams = contents.Kind == PdfValueKind.Array ? contents.Items : [source];
                using MemoryStream combined = new();
                foreach (PdfValue reference in streams)
                {
                    byte[] bytes = storage.Decode(storage.Object(reference, token), token);
                    if (combined.Length + bytes.Length + 1 > storage.Budget.Options.MaximumDecodedBytes) { throw new InvalidDataException("PDF page contents exceed their limit."); }
                    combined.Write(bytes); combined.WriteByte(10);
                }
                Execute(combined.ToArray(), page.Resources, 0);
            }
            return new(page.Info, _commands, _unsupported, _images);
        }
        private PdfValue Resolve(PdfValue value) => storage.Resolve(value, token);
        private PdfValue Get(PdfValue value, string key) => Resolve(value.Get(key));
        private static float Float(double value)
        { if (!double.IsFinite(value) || value < -1e15 || value > 1e15) { throw new InvalidDataException("PDF coordinate is too large."); } return (float)value; }
        private Matrix3x2 Matrix(PdfValue value)
        {
            PdfValue[] items = Resolve(value).Items;
            if (items.Length != 6) { throw new InvalidDataException("Invalid PDF transformation matrix."); }
            return new(Float(items[0].Numeric()), Float(items[1].Numeric()), Float(items[2].Numeric()), Float(items[3].Numeric()), Float(items[4].Numeric()), Float(items[5].Numeric()));
        }
        private void Execute(ReadOnlyMemory<byte> bytes, PdfValue resources, int depth)
        {
            if (depth >= storage.Budget.Options.MaximumDepth) { throw new InvalidDataException("Excessive PDF Form nesting."); }
            PdfSyntax syntax = new(bytes, storage.Budget, token);
            List<PdfValue> operands = [];
            Stack<PdfGraphicsState> saved = [];
            int compatibility = 0;
            while (true)
            {
                syntax.Skip(); if (syntax.Position == bytes.Length) { break; }
                PdfValue value = syntax.Read();
                if (value.Kind != PdfValueKind.Keyword)
                { operands.Add(value); if (operands.Count > 4096) { throw new InvalidDataException("Too many PDF operands."); } continue; }
                if (++_operations > storage.Budget.Options.MaximumOperations) { throw new InvalidDataException("PDF page exceeds its operation limit."); }
                double N(int i) => Resolve(operands[i]).Numeric();
                float F(int i) => Float(N(i));
                string Name(int i) => Resolve(operands[i]).Name();
                void Count(int expected) { if (operands.Count != expected) { throw new InvalidDataException("Invalid operand count for PDF operator " + value.Text); } }
                Vector2 Point(int i) => Vector2.Transform(new(F(i), F(i + 1)), _state.Transform);
                switch (value.Text)
                {
                    case "q":
                        Count(0); if (saved.Count >= storage.Budget.Options.MaximumDepth) { throw new InvalidDataException("Excessive PDF graphics state stack."); }
                        saved.Push(_state); _commands.Add(new StateCommand(true)); break;
                    case "Q":
                        Count(0); if (!saved.TryPop(out PdfGraphicsState? restore)) { throw new InvalidDataException("Unbalanced PDF graphics state."); }
                        _state = restore; _commands.Add(new StateCommand(false)); break;
                    case "cm": Count(6); _state = _state with { Transform = new Matrix3x2(F(0), F(1), F(2), F(3), F(4), F(5)) * _state.Transform }; break;
                    case "m": Count(2); _current = _start = Point(0); _path.MoveTo(_current.X, _current.Y); _hasPoint = true; break;
                    case "l": Count(2); RequirePoint(); _current = Point(0); _path.LineTo(_current.X, _current.Y); break;
                    case "c": Count(6); RequirePoint(); Curve(Point(0), Point(2), Point(4)); break;
                    case "v": Count(4); RequirePoint(); Curve(_current, Point(0), Point(2)); break;
                    case "y": Count(4); RequirePoint(); Curve(Point(0), Point(2), Point(2)); break;
                    case "h": Count(0); Close(); break;
                    case "re":
                        Count(4); Vector2 a = Point(0), b = Vector2.Transform(new(F(0) + F(2), F(1)), _state.Transform), c = Vector2.Transform(new(F(0) + F(2), F(1) + F(3)), _state.Transform), d = Vector2.Transform(new(F(0), F(1) + F(3)), _state.Transform);
                        _path.MoveTo(a.X, a.Y).LineTo(b.X, b.Y).LineTo(c.X, c.Y).LineTo(d.X, d.Y).Close(); _current = _start = a; _hasPoint = true; break;
                    case "W": Count(0); _pendingClip = FillRule.NonZero; break;
                    case "W*": Count(0); _pendingClip = FillRule.EvenOdd; break;
                    case "n": Count(0); Finish(false, false, FillRule.NonZero); break;
                    case "f": case "F": Count(0); Finish(true, false, FillRule.NonZero); break;
                    case "f*": Count(0); Finish(true, false, FillRule.EvenOdd); break;
                    case "S": Count(0); Finish(false, true, FillRule.NonZero); break;
                    case "s": Count(0); Close(); Finish(false, true, FillRule.NonZero); break;
                    case "B": case "B*": Count(0); Finish(true, true, value.Text == "B" ? FillRule.NonZero : FillRule.EvenOdd); break;
                    case "b": case "b*": Count(0); Close(); Finish(true, true, value.Text == "b" ? FillRule.NonZero : FillRule.EvenOdd); break;
                    case "w": Count(1); if (F(0) < 0) { throw new InvalidDataException("Negative PDF line width."); } _state = _state with { Pen = _state.Pen with { Width = F(0) } }; break;
                    case "J": Count(1); _state = _state with { Pen = _state.Pen with { Cap = (int)N(0) switch { 0 => StrokeCap.Butt, 1 => StrokeCap.Round, 2 => StrokeCap.Square, _ => throw new InvalidDataException("Invalid PDF line cap.") } } }; break;
                    case "j": Count(1); _state = _state with { Pen = _state.Pen with { Join = (int)N(0) switch { 0 => StrokeJoin.Miter, 1 => StrokeJoin.Round, 2 => StrokeJoin.Bevel, _ => throw new InvalidDataException("Invalid PDF line join.") } } }; break;
                    case "M": Count(1); if (F(0) < 1) { throw new InvalidDataException("Invalid PDF miter limit."); } _state = _state with { Pen = _state.Pen with { MiterLimit = F(0) } }; break;
                    case "d": Count(2); float[] dash = Resolve(operands[0]).Items.Select(x => Float(x.Numeric())).ToArray(); if (dash.Any(x => x < 0) || dash.Length > 0 && dash.All(x => x == 0)) { throw new InvalidDataException("Invalid PDF dash array."); } _state = _state with { Pen = _state.Pen with { Dashes = dash, DashOffset = F(1) } }; break;
                    case "rg": case "RG": Count(3); Color(new(Channel(N(0)), Channel(N(1)), Channel(N(2))), value.Text == "RG"); break;
                    case "g": case "G": Count(1); Color(new(Channel(N(0)), Channel(N(0)), Channel(N(0))), value.Text == "G"); break;
                    case "k": case "K": Count(4); Color(new(Channel(1 - Math.Min(1, N(0) + N(3))), Channel(1 - Math.Min(1, N(1) + N(3))), Channel(1 - Math.Min(1, N(2) + N(3)))), value.Text == "K"); break;
                    case "gs": Count(1); ExtState(Get(Get(resources, "ExtGState"), Name(0))); break;
                    case "Do": Count(1); XObject(Name(0), resources, depth); break;
                    case "BT": Count(0); if (_inText) { throw new InvalidDataException("Nested PDF text object."); } _inText = true; _text = _textLine = Matrix3x2.Identity; break;
                    case "ET": Count(0); RequireText(); _inText = false; if (_hasTextClip) { _commands.Add(new ClipCommand(_textClip, FillRule.NonZero)); _hasTextClip = false; _textClip = new(); } break;
                    case "Tf": Count(2); _state = _state with { Font = ReadFont(Get(Get(resources, "Font"), Name(0))), FontSize = F(1) }; break;
                    case "Tm": Count(6); RequireText(); _text = _textLine = new(F(0), F(1), F(2), F(3), F(4), F(5)); break;
                    case "Td": case "TD": Count(2); RequireText(); if (value.Text == "TD") { _state = _state with { Leading = -F(1) }; } MoveText(F(0), F(1)); break;
                    case "T*": Count(0); RequireText(); MoveText(0, -_state.Leading); break;
                    case "Tc": Count(1); _state = _state with { CharacterSpace = F(0) }; break;
                    case "Tw": Count(1); _state = _state with { WordSpace = F(0) }; break;
                    case "Tz": Count(1); _state = _state with { HorizontalScale = F(0) / 100 }; break;
                    case "TL": Count(1); _state = _state with { Leading = F(0) }; break;
                    case "Ts": Count(1); _state = _state with { Rise = F(0) }; break;
                    case "Tr": Count(1); int mode = Resolve(operands[0]).Integer(); if (mode is < 0 or > 7) { throw new InvalidDataException("Invalid PDF text mode."); } _state = _state with { TextMode = mode }; break;
                    case "Tj": Count(1); Show(Resolve(operands[0])); break;
                    case "TJ": Count(1); RequireText(); foreach (PdfValue part in Resolve(operands[0]).Items) { if (part.Kind == PdfValueKind.Number) { Advance(-Float(part.Number) * _state.FontSize * _state.HorizontalScale / 1000); } else { Show(part); } } break;
                    case "'": Count(1); MoveText(0, -_state.Leading); Show(Resolve(operands[0])); break;
                    case "\"": Count(3); _state = _state with { WordSpace = F(0), CharacterSpace = F(1) }; MoveText(0, -_state.Leading); Show(Resolve(operands[2])); break;
                    case "BX": Count(0); compatibility++; break;
                    case "EX": Count(0); if (--compatibility < 0) { throw new InvalidDataException("Unbalanced PDF compatibility section."); } break;
                    case "BMC": case "BDC": case "EMC": case "MP": case "DP": break;
                    case "ri": Count(1); _unsupported.Add("Rendering intent: " + Name(0)); break;
                    case "i": Count(1); break;
                    case "BI": throw new NotSupportedException("Inline PDF images are not supported yet.");
                    default: if (compatibility == 0) { _unsupported.Add("Content operator: " + value.Text); } break;
                }
                operands.Clear();
            }
            if (operands.Count != 0 || saved.Count != 0 || compatibility != 0 || _inText) { throw new InvalidDataException("Unbalanced PDF content stream."); }
        }
        private void RequirePoint() { if (!_hasPoint) { throw new InvalidDataException("PDF path has no current point."); } }
        private void Close() { RequirePoint(); _path.Close(); _current = _start; }
        private void Curve(Vector2 a, Vector2 b, Vector2 end) { _path.CubicTo(a.X, a.Y, b.X, b.Y, end.X, end.Y); _current = end; }
        private void Finish(bool fill, bool stroke, FillRule rule)
        {
            if (fill) { _commands.Add(new FillCommand(_path, _state.Fill with { A = Channel(_state.FillAlpha) }, rule, _state.Blend)); }
            if (stroke) { _commands.Add(new StrokeCommand(_path, _state.Stroke with { A = Channel(_state.StrokeAlpha) }, _state.Pen, _state.Transform, _state.Blend)); }
            if (_pendingClip is FillRule clip) { _commands.Add(new ClipCommand(_path, clip)); }
            _path = new(); _hasPoint = false; _pendingClip = null;
        }
        private void Color(Rgba32 value, bool stroke) { _state = stroke ? _state with { Stroke = value } : _state with { Fill = value }; }
        private static byte Channel(double value) => (byte)Math.Clamp(Math.Round(value * 255), 0, 255);
        private void ExtState(PdfValue state)
        {
            if (state.Kind != PdfValueKind.Dictionary) { throw new InvalidDataException("Missing PDF extended graphics state."); }
            foreach ((string key, PdfValue raw) in state.Entries)
            {
                PdfValue value = Resolve(raw);
                switch (key)
                {
                    case "Type": break;
                    case "ca": _state = _state with { FillAlpha = Math.Clamp(Float(value.Numeric()), 0, 1) }; break;
                    case "CA": _state = _state with { StrokeAlpha = Math.Clamp(Float(value.Numeric()), 0, 1) }; break;
                    case "BM":
                        if (value.Kind == PdfValueKind.Array) { _unsupported.Add("Blend mode preference array"); break; }
                        _state = _state with { Blend = Blend(value.Name()) }; break;
                    case "SMask": if (value.Kind != PdfValueKind.Name || value.Text != "None") { _unsupported.Add("Graphics-state soft mask"); } break;
                    default: _unsupported.Add("Extended graphics state: " + key); break;
                }
            }
        }
        private static ColorBlendMode Blend(string name) => name switch
        {
            "Normal" or "Compatible" => ColorBlendMode.Normal,
            "Multiply" => ColorBlendMode.Multiply,
            "Screen" => ColorBlendMode.Screen,
            "Overlay" => ColorBlendMode.Overlay,
            "Darken" => ColorBlendMode.Darken,
            "Lighten" => ColorBlendMode.Lighten,
            "ColorDodge" => ColorBlendMode.ColorDodge,
            "ColorBurn" => ColorBlendMode.ColorBurn,
            "HardLight" => ColorBlendMode.HardLight,
            "SoftLight" => ColorBlendMode.SoftLight,
            "Difference" => ColorBlendMode.Difference,
            "Exclusion" => ColorBlendMode.Exclusion,
            "Hue" => ColorBlendMode.Hue,
            "Saturation" => ColorBlendMode.Saturation,
            "Color" => ColorBlendMode.Color,
            "Luminosity" => ColorBlendMode.Luminosity,
            _ => throw new NotSupportedException("Unsupported PDF blend mode: " + name)
        };
        private void XObject(string name, PdfValue resources, int depth)
        {
            PdfValue reference = Get(resources, "XObject").Get(name);
            PdfObject obj = storage.Object(reference, token);
            string subtype = Get(obj.Value, "Subtype").Name();
            if (subtype == "Image")
            {
                PdfImage image = Image(obj, name, 0);
                Matrix3x2 placement = new Matrix3x2(1f / image.Width, 0, 0, -1f / image.Height, 0, 1) * _state.Transform;
                _commands.Add(new ImageCommand(image.Buffer, placement, _state.FillAlpha, _state.Blend));
            }
            else if (subtype == "Form")
            {
                if (!_forms.Add(obj)) { throw new InvalidDataException("Recursive PDF Form XObject."); }
                PdfGraphicsState previous = _state;
                Matrix3x2 text = _text, line = _textLine;
                bool inText = _inText, hasTextClip = _hasTextClip;
                VectorPath textClip = _textClip;
                VectorPath path = _path; bool point = _hasPoint; FillRule? clip = _pendingClip; Vector2 current = _current, start = _start;
                try
                {
                    if (obj.Value.Get("Group").Kind != PdfValueKind.Null) { _unsupported.Add("Form transparency groups"); }
                    if (obj.Value.Get("OC").Kind != PdfValueKind.Null) { _unsupported.Add("Form optional content"); }
                    _commands.Add(new StateCommand(true));
                    if (obj.Value.Get("Matrix").Kind != PdfValueKind.Null) { _state = _state with { Transform = Matrix(obj.Value.Get("Matrix")) * previous.Transform }; }
                    double[] box = PdfDocument.Box(Get(obj.Value, "BBox"));
                    VectorPath bounds = new VectorPath().Rectangle(Float(box[0]), Float(box[1]), Float(box[2] - box[0]), Float(box[3] - box[1])).Transform(_state.Transform);
                    _commands.Add(new ClipCommand(bounds, FillRule.NonZero));
                    _path = new(); _hasPoint = false; _pendingClip = null;
                    _inText = false; _hasTextClip = false; _textClip = new();
                    PdfValue local = obj.Value.Get("Resources").Kind == PdfValueKind.Null ? resources : Get(obj.Value, "Resources");
                    Execute(storage.Decode(obj, token), local, depth + 1);
                    _commands.Add(new StateCommand(false));
                }
                finally
                {
                    _state = previous; _path = path; _hasPoint = point; _pendingClip = clip; _current = current; _start = start;
                    _text = text; _textLine = line; _inText = inText; _hasTextClip = hasTextClip; _textClip = textClip; _forms.Remove(obj);
                }
            }
            else { throw new NotSupportedException("Unsupported PDF XObject subtype: " + subtype); }
        }
    }
}
