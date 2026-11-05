// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Core.Metadata
{
    /// <summary>Lossless metadata scalar. Binary values own their bytes; rationals retain the declared fraction.</summary>
    public readonly record struct MediaPropertyValue
    {
        private readonly string? text;
        private readonly long integer;
        private readonly ulong unsignedInteger;
        private readonly double number;
        private readonly long denominator;
        private readonly ReadOnlyMemory<byte> binary;

        private MediaPropertyValue(MediaPropertyValueKind kind, string? text = null, long integer = 0,
            ulong unsignedInteger = 0, double number = 0, long denominator = 0, ReadOnlyMemory<byte> binary = default)
        {
            Kind = kind;
            this.text = text;
            this.integer = integer;
            this.unsignedInteger = unsignedInteger;
            this.number = number;
            this.denominator = denominator;
            this.binary = binary;
        }

        public MediaPropertyValueKind Kind { get; }

        public static implicit operator MediaPropertyValue(string value)
        {
            ArgumentNullException.ThrowIfNull(value);
            return new(MediaPropertyValueKind.Text, text: value);
        }

        public static implicit operator MediaPropertyValue(int value) => (long)value;
        public static implicit operator MediaPropertyValue(uint value) => (ulong)value;
        public static implicit operator MediaPropertyValue(long value) => new(MediaPropertyValueKind.Integer, integer: value);
        public static implicit operator MediaPropertyValue(ulong value) => new(MediaPropertyValueKind.UnsignedInteger, unsignedInteger: value);
        public static implicit operator MediaPropertyValue(bool value) => new(MediaPropertyValueKind.Boolean, integer: value ? 1 : 0);
        public static implicit operator MediaPropertyValue(double value)
        {
            if (!double.IsFinite(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            return new(MediaPropertyValueKind.Number, number: value);
        }

        public static MediaPropertyValue FromRational(long numerator, long denominator)
        {
            ArgumentOutOfRangeException.ThrowIfZero(denominator);
            return new(MediaPropertyValueKind.Rational, integer: numerator, denominator: denominator);
        }

        public static MediaPropertyValue FromBytes(ReadOnlySpan<byte> bytes) => new(MediaPropertyValueKind.Binary, binary: bytes.ToArray());
        public string AsText() { Require(MediaPropertyValueKind.Text); return text!; }
        public long AsInteger() { Require(MediaPropertyValueKind.Integer); return integer; }
        public ulong AsUnsignedInteger() { Require(MediaPropertyValueKind.UnsignedInteger); return unsignedInteger; }
        public double AsNumber() { Require(MediaPropertyValueKind.Number); return number; }
        public bool AsBoolean() { Require(MediaPropertyValueKind.Boolean); return integer != 0; }
        public (long Numerator, long Denominator) AsRational() { Require(MediaPropertyValueKind.Rational); return (integer, denominator); }
        public ReadOnlyMemory<byte> AsBytes() { Require(MediaPropertyValueKind.Binary); return binary; }

        private void Require(MediaPropertyValueKind expected)
        {
            if (Kind != expected)
            {
                throw new InvalidOperationException($"Metadata value is {Kind}, not {expected}.");
            }
        }
    }
}
