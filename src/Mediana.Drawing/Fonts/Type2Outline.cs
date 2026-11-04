// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.IO;

namespace Mediana.Drawing.Fonts
{
    internal partial class Type2Outline(ReadOnlyMemory<byte>[] local, ReadOnlyMemory<byte>[] global)
    {
        private readonly double[] _stack = new double[48];
        private readonly VectorPath _path = new();
        private int _count;
        private int _hints;
        private int _operations;
        private bool _widthRead;
        private bool _open;
        private double _x;
        private double _y;

        internal VectorPath Read(ReadOnlyMemory<byte> program)
        {
            if (!Execute(program.Span, 0)) { throw new InvalidDataException("CFF glyph did not end with endchar."); }
            return _path;
        }

        private bool Execute(ReadOnlySpan<byte> program, int depth)
        {
            if (depth > 10) { throw new InvalidDataException("CFF subroutine nesting exceeds ten calls."); }
            int position = 0;
            while (position < program.Length)
            {
                if (++_operations > 100_000) { throw new InvalidDataException("CFF glyph operation limit exceeded."); }
                int code = program[position++];
                if (code >= 32 || code == 28)
                {
                    if (_count == _stack.Length) { throw new InvalidDataException("CFF operand stack exceeds 48 entries."); }
                    _stack[_count++] = CffData.Number(program, ref position, code);
                    continue;
                }
                if (code == 12) { CffData.Check(program.Length, position, 1); code = 1200 + program[position++]; }
                switch (code)
                {
                    case 1: case 3: case 18: case 23: Stems(false); break;
                    case 19:
                    case 20:
                        Stems(true);
                        int length = (_hints + 7) / 8;
                        CffData.Check(program.Length, position, length); position += length;
                        break;
                    case 4: Width(1); Count(1); Move(0, _stack[0]); break;
                    case 21: Width(2); Count(2); Move(_stack[0], _stack[1]); break;
                    case 22: Width(1); Count(1); Move(_stack[0], 0); break;
                    case 5:
                        Multiple(2, 2);
                        for (int i = 0; i < _count; i += 2) { Line(_stack[i], _stack[i + 1]); }
                        break;
                    case 6:
                    case 7:
                        if (_count == 0) { throw new InvalidDataException("CFF line requires operands."); }
                        bool horizontal = code == 6;
                        for (int i = 0; i < _count; i++, horizontal = !horizontal)
                        { if (horizontal) { Line(_stack[i], 0); } else { Line(0, _stack[i]); } }
                        break;
                    case 8:
                        Multiple(6, 6);
                        for (int i = 0; i < _count; i += 6) { Curve(i); }
                        break;
                    case 24:
                        if (_count < 8 || (_count - 2) % 6 != 0) { throw new InvalidDataException("Invalid CFF rcurveline operands."); }
                        for (int i = 0; i < _count - 2; i += 6) { Curve(i); }
                        Line(_stack[_count - 2], _stack[_count - 1]); break;
                    case 25:
                        if (_count < 8 || (_count - 6) % 2 != 0) { throw new InvalidDataException("Invalid CFF rlinecurve operands."); }
                        for (int i = 0; i < _count - 6; i += 2) { Line(_stack[i], _stack[i + 1]); }
                        Curve(_count - 6); break;
                    case 26: case 27: AxisCurves(code == 27); break;
                    case 30: case 31: AlternatingCurves(code == 31); break;
                    case 1234: case 1235: case 1236: case 1237: Flex(code); break;
                    case 10:
                    case 29:
                        if (_count == 0) { throw new InvalidDataException("CFF subroutine requires an index."); }
                        ReadOnlyMemory<byte>[] subrs = code == 10 ? local : global;
                        double operand = _stack[--_count];
                        int bias = subrs.Length < 1240 ? 107 : subrs.Length < 33900 ? 1131 : 32768;
                        double index = operand + bias;
                        if (index != Math.Truncate(index) || index < 0 || index >= subrs.Length) { throw new InvalidDataException("Invalid CFF subroutine index."); }
                        if (Execute(subrs[(int)index].Span, depth + 1)) { return true; }
                        continue;
                    case 11:
                        if (depth == 0) { throw new InvalidDataException("CFF return outside a subroutine."); }
                        return false;
                    case 14:
                        Width(0);
                        if (_count == 4) { throw new NotSupportedException("CFF composite endchar outlines are not implemented."); }
                        Count(0); Close(); return true;
                    default: throw new NotSupportedException("Unsupported Type 2 charstring operator: " + code);
                }
                _count = 0;
            }
            throw new InvalidDataException("Unterminated CFF charstring or subroutine.");
        }

        private void Stems(bool allowEmpty)
        {
            if (!_widthRead && (_count & 1) != 0) { RemoveWidth(); }
            _widthRead = true;
            if ((_count & 1) != 0 || !allowEmpty && _count == 0) { throw new InvalidDataException("Invalid CFF stem operands."); }
            _hints += _count / 2;
            if (_hints > 96) { throw new InvalidDataException("CFF stem hint limit exceeded."); }
        }
        private void Width(int count)
        {
            if (!_widthRead && _count == count + 1) { RemoveWidth(); }
            _widthRead = true;
        }
        private void RemoveWidth() { Array.Copy(_stack, 1, _stack, 0, --_count); }
        private void Count(int expected)
        {
            if (_count != expected) { throw new InvalidDataException("Invalid CFF operator operand count."); }
        }
        private void Multiple(int minimum, int size)
        {
            if (_count < minimum || _count % size != 0) { throw new InvalidDataException("Invalid CFF path operand count."); }
        }
        private static float F(double value)
        {
            if (!double.IsFinite(value) || Math.Abs(value) > float.MaxValue) { throw new InvalidDataException("Invalid CFF outline coordinate."); }
            return (float)value;
        }
        private void Close() { if (_open) { _path.Close(); _open = false; } }
        private void Move(double x, double y)
        {
            Close(); _x += x; _y += y;
            _path.MoveTo(F(_x), F(_y)); _open = true;
        }
        private void Line(double x, double y)
        {
            if (!_open) { throw new InvalidDataException("CFF path has no moveto."); }
            _x += x; _y += y; _path.LineTo(F(_x), F(_y));
        }
        private void Curve(int i) => Curve(_stack[i], _stack[i + 1], _stack[i + 2], _stack[i + 3], _stack[i + 4], _stack[i + 5]);
        private void Curve(double dx1, double dy1, double dx2, double dy2, double dx3, double dy3)
        {
            if (!_open) { throw new InvalidDataException("CFF curve has no moveto."); }
            double x1 = _x + dx1, y1 = _y + dy1, x2 = x1 + dx2, y2 = y1 + dy2;
            _x = x2 + dx3; _y = y2 + dy3;
            _path.CubicTo(F(x1), F(y1), F(x2), F(y2), F(_x), F(_y));
        }
    }
}
