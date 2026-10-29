// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Entropy;
using System;
using System.IO;

namespace Mediana.Codecs.Jpeg2000
{
    internal class RawBits
    {
        private readonly byte[] _data;
        private int _position;
        private int _bits;
        private int _value;
        internal RawBits(byte[] data) => _data = data;
        internal int Read()
        {
            if (_bits == 0)
            {
                _bits = _value == 255 ? 7 : 8;
                _value = _position < _data.Length ? _data[_position++] : 255;
            }

            return (_value >> --_bits) & 1;
        }
    }
}
