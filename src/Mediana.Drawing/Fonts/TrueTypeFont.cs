// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Drawing.Fonts
{
    /// <summary>Single-consumer outline and kerning caches over a shared immutable font program.</summary>
    public partial class TrueTypeFont
    {
        private readonly Dictionary<int, VectorPath> _outlines = new();
        private readonly Dictionary<(int, int), float> _pairs = new();
        private readonly FontProgram _program;
        private byte[] _data => _program.Data;
        private IReadOnlyDictionary<string, (int Start, int Length)> _tables => _program.Tables;
        private int _glyphs => _program.GlyphCount;
        private int _locaFormat => _program.LocaFormat;
        private CffFont? _cff => _program.Cff;
        public float Units => _program.Units;
        public float Ascent => _program.Ascent;
        public float Descent => _program.Descent;
        public float XHeight => _program.XHeight;
        public ReadOnlyMemory<byte> Program => _program.Program;
        public FontProgram SharedProgram => _program;

        public TrueTypeFont(ReadOnlyMemory<byte> source) : this(new FontProgram(source)) { }

        public TrueTypeFont(FontProgram program)
        {
            ArgumentNullException.ThrowIfNull(program);
            _program = program;
        }

        public FontFaceInfo? ReadFaceInfo() => _program.ReadFaceInfo();
        public int Glyph(int codepoint) => _program.Glyph(codepoint);
        public int EncodedGlyph(int code, int platform, int encoding) => _program.EncodedGlyph(code, platform, encoding);
        public float Advance(int glyph) => _program.Advance(glyph);
        public VectorPath Outline(int glyph) => BorrowOutline(glyph).Transform(Matrix3x2.Identity);

        internal VectorPath BorrowOutline(int glyph)
        {
            if (!_outlines.TryGetValue(glyph, out VectorPath? path))
            {
                if (_cff is not null) { path = _cff.Outline(glyph); }
                else { path = Path(ReadGlyph(glyph, new HashSet<int>(), 0)); }
                _outlines.Add(glyph, path);
            }
            return path;
        }

        private int Table(string name, int minimum) => _program.Table(name, minimum);
        private void Check(int offset, int count) => _program.Check(offset, count);
        private byte Byte(int position) => _program.Byte(position);
        private int U16(int position) => _program.U16(position);
        private int S16(int position) => _program.S16(position);
        private uint U32(int position) => _program.U32(position);
        private int Offset(int position) => _program.Offset(position);
        private string Tag(int position) => _program.Tag(position);
    }
}
