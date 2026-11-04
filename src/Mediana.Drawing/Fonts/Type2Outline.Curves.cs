// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.IO;

namespace Mediana.Drawing.Fonts
{
    internal partial class Type2Outline
    {
        private void AxisCurves(bool horizontal)
        {
            if (_count < 4 || _count % 4 is not (0 or 1)) { throw new InvalidDataException("Invalid CFF axis curve operands."); }
            int i = 0;
            double first = (_count & 1) != 0 ? _stack[i++] : 0;
            for (; i < _count; i += 4)
            {
                if (horizontal) { Curve(_stack[i], first, _stack[i + 1], _stack[i + 2], _stack[i + 3], 0); }
                else { Curve(first, _stack[i], _stack[i + 1], _stack[i + 2], 0, _stack[i + 3]); }
                first = 0;
            }
        }

        private void AlternatingCurves(bool horizontal)
        {
            if (_count < 4 || _count % 4 is not (0 or 1)) { throw new InvalidDataException("Invalid CFF alternating curve operands."); }
            for (int i = 0; i < _count - 1; i += 4, horizontal = !horizontal)
            {
                double last = i + 5 == _count ? _stack[i + 4] : 0;
                if (horizontal) { Curve(_stack[i], 0, _stack[i + 1], _stack[i + 2], last, _stack[i + 3]); }
                else { Curve(0, _stack[i], _stack[i + 1], _stack[i + 2], _stack[i + 3], last); }
            }
        }

        private void Flex(int code)
        {
            switch (code)
            {
                case 1234:
                    Count(7);
                    Curve(_stack[0], 0, _stack[1], _stack[2], _stack[3], 0);
                    Curve(_stack[4], 0, _stack[5], -_stack[2], _stack[6], 0);
                    break;
                case 1235:
                    Count(13); Curve(0); Curve(6); break;
                case 1236:
                    Count(9);
                    Curve(_stack[0], _stack[1], _stack[2], _stack[3], _stack[4], 0);
                    Curve(_stack[5], 0, _stack[6], _stack[7], _stack[8], -_stack[1] - _stack[3] - _stack[7]);
                    break;
                case 1237:
                    Count(11);
                    double x = _stack[0] + _stack[2] + _stack[4] + _stack[6] + _stack[8];
                    double y = _stack[1] + _stack[3] + _stack[5] + _stack[7] + _stack[9];
                    Curve(0);
                    if (Math.Abs(x) > Math.Abs(y)) { Curve(_stack[6], _stack[7], _stack[8], _stack[9], _stack[10], -y); }
                    else { Curve(_stack[6], _stack[7], _stack[8], _stack[9], -x, _stack[10]); }
                    break;
                default: throw new InvalidDataException("Invalid CFF flex operator.");
            }
        }
    }
}
