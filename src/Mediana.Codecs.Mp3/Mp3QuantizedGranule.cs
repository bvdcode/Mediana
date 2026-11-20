// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Mp3
{
    internal readonly record struct Mp3QuantizedGranule(int Bits, int BigValues, int Gain, int Table0, int Table1, int Table2);
}
