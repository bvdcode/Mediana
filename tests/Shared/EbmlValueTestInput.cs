// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using System.Text;
using Mediana.Containers.Matroska;

namespace Mediana.Tests
{
    internal static class EbmlValueTestInput
    {
        public static void VerifyVariableIntegers()
        {
            byte[][] padded = [[0x82], [0x40, 2], [0x20, 0, 2], [0x10, 0, 0, 2],
                [8, 0, 0, 0, 2], [4, 0, 0, 0, 0, 2], [2, 0, 0, 0, 0, 0, 2], [1, 0, 0, 0, 0, 0, 0, 2]];
            for (int index = 0; index < padded.Length; index++)
            {
                EbmlVint value = EbmlInteger.ReadVint(padded[index]);
                Require(value.Value == 2 && value.Width == index + 1 && !value.IsUnknown);
                Require(EbmlInteger.ReadSize(padded[index], out int width) == 2 && width == index + 1);
                for (int length = 0; length < padded[index].Length; length++)
                {
                    int truncated = length;
                    Reject<EndOfStreamException>(() => EbmlInteger.ReadVint(padded[index].AsSpan(0, truncated)));
                }
            }
            byte[][] unknown = [[0xFF], [0x7F, 0xFF], [0x3F, 0xFF, 0xFF], [0x1F, 0xFF, 0xFF, 0xFF],
                [0x0F, 0xFF, 0xFF, 0xFF, 0xFF], [7, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF],
                [3, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF], [1, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF]];
            foreach (byte[] encoded in unknown)
            {
                Require(EbmlInteger.ReadVint(encoded).IsUnknown);
                Require(EbmlInteger.ReadSize(encoded, out int width) is null && width == encoded.Length);
            }
            Require(EbmlInteger.ReadSize([0x80], out _) == 0);
            Require(EbmlInteger.ReadSize([0x40, 0x7F], out _) == 127);
            Require(EbmlInteger.ReadSize([1, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFE], out _) == 72057594037927934);
        }

        public static void VerifyIdentifiers()
        {
            (byte[] Bytes, uint Id)[] valid = [([0x80], 0x80), ([0x81], 0x81), ([0xFE], 0xFE),
                ([0x40, 0x7F], 0x407F), ([0x42, 0x86], 0x4286), ([0x7F, 0xFE], 0x7FFE),
                ([0x20, 0x3F, 0xFF], 0x203FFF), ([0x3F, 0xFF, 0xFE], 0x3FFFFE),
                ([0x10, 0x1F, 0xFF, 0xFF], 0x101FFFFF), ([0x1A, 0x45, 0xDF, 0xA3], 0x1A45DFA3),
                ([0x1F, 0xFF, 0xFF, 0xFE], 0x1FFFFFFE)];
            foreach ((byte[] bytes, uint id) in valid)
            {
                Require(EbmlInteger.ReadId(bytes, out int width) == id && width == bytes.Length);
            }
            byte[][] invalid = [[0], [0xFF], [0x40, 0], [0x40, 0x7E], [0x7F, 0xFF],
                [0x20, 0x3F, 0xFE], [0x3F, 0xFF, 0xFF], [0x10, 0x1F, 0xFF, 0xFE],
                [0x1F, 0xFF, 0xFF, 0xFF], [8, 0, 0, 0, 1]];
            foreach (byte[] bytes in invalid)
            {
                Reject<InvalidDataException>(() => EbmlInteger.ReadId(bytes, out _));
            }
        }

        public static void VerifyElementValues()
        {
            Require(EbmlInteger.ReadUnsigned([], 48000) == 48000);
            Require(EbmlInteger.ReadSigned([], -17) == -17);
            for (int length = 1; length <= 8; length++)
            {
                byte[] bytes = new byte[length];
                for (int value = 0; value <= byte.MaxValue; value++)
                {
                    bytes.AsSpan().Fill((byte)value);
                    BigInteger unsigned = new(bytes, isUnsigned: true, isBigEndian: true);
                    BigInteger signed = new(bytes, isUnsigned: false, isBigEndian: true);
                    Require(EbmlInteger.ReadUnsigned(bytes, 123) == unsigned);
                    Require(EbmlInteger.ReadSigned(bytes, 123) == signed);
                }
            }
            Require(EbmlValue.ReadFloat([], 44100) == 44100);
            Require(EbmlValue.ReadFloat([0x40, 0x20, 0, 0], 0) == 2.5);
            Require(EbmlValue.ReadFloat([0xC0, 4, 0, 0, 0, 0, 0, 0], 0) == -2.5);
            Require(BitConverter.DoubleToInt64Bits(EbmlValue.ReadFloat([0x80, 0, 0, 0], 1)) == long.MinValue);
            Require(double.IsPositiveInfinity(EbmlValue.ReadFloat([0x7F, 0x80, 0, 0], 0)));
            Require(double.IsNaN(EbmlValue.ReadFloat([0x7F, 0xC0, 0, 1], 0)));
        }

        public static void VerifyStrings()
        {
            Require(EbmlValue.ReadString([]) == string.Empty);
            Require(EbmlValue.ReadString([0x65, 0x62, 0x6D, 0x6C]) == "ebml");
            Require(EbmlValue.ReadString([0x65, 0x62, 0, 0x6C]) == "eb");
            Require(EbmlValue.ReadString([0x65, 0x62, 0, 0]) == "eb");
            Require(EbmlValue.ReadString([0x20, 0x7E]) == " ~");
            Require(EbmlValue.ReadString([0, 0xFF]) == string.Empty);
            Require(EbmlValue.ReadUtf8(Encoding.UTF8.GetBytes("Медиа 🎵")) == "Медиа 🎵");
            Require(EbmlValue.ReadUtf8([0xC3, 0xA9, 0, 0xFF]) == "é");
            foreach (byte[] bytes in new byte[][] { [0xC0, 0x80], [0xED, 0xA0, 0x80], [0xF5, 0x80, 0x80, 0x80], [0xC3], [0x80] })
            {
                Reject<InvalidDataException>(() => EbmlValue.ReadUtf8(bytes));
            }
            foreach (byte[] bytes in new byte[][] { [0x1F], [0x7F], [0x80], [9], [0xFF] })
            {
                Reject<InvalidDataException>(() => EbmlValue.ReadString(bytes));
            }
        }

        public static void VerifyInvalidEncodings()
        {
            Reject<InvalidDataException>(() => EbmlInteger.ReadVint([0, 0xFF]));
            Reject<InvalidDataException>(() => EbmlInteger.ReadVint([0x40, 1], 1));
            Reject<ArgumentOutOfRangeException>(() => EbmlInteger.ReadVint([0x80], 0));
            Reject<ArgumentOutOfRangeException>(() => EbmlInteger.ReadVint([0x80], 9));
            Reject<ArgumentOutOfRangeException>(() => EbmlInteger.ReadId([0x80], out _, 5));
            Reject<InvalidDataException>(() => EbmlInteger.ReadUnsigned(new byte[9], 0));
            Reject<InvalidDataException>(() => EbmlInteger.ReadSigned(new byte[9], 0));
            foreach (int length in new[] { 1, 2, 3, 5, 6, 7, 9 })
            {
                Reject<InvalidDataException>(() => EbmlValue.ReadFloat(new byte[length], 0));
            }
        }

        private static void Require(bool condition)
        {
            if (!condition)
            {
                throw new InvalidDataException("An EBML value differs from its independent reference.");
            }
        }

        private static void Reject<TException>(Action action) where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                return;
            }
            throw new InvalidDataException("An invalid EBML value was accepted.");
        }
    }
}
