// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Asf
{
    internal class AsfMediaObject(int streamIndex, AsfPayload first)
    {
        public int StreamIndex { get; } = streamIndex;
        public int Number { get; } = first.Object;
        public long? Timestamp { get; } = first.Timestamp;
        public bool KeyFrame { get; } = first.KeyFrame;
        public int? ExpectedLength { get; } = first.Size;
        public int Length { get; private set; }
        public List<AsfPayload> Parts { get; } = [];

        public void Add(AsfPayload part)
        {
            if (Number != part.Object || part.Offset != Length ||
                (part.Size is int declared && ExpectedLength != declared) ||
                (ExpectedLength is int expected && part.Length > expected - Length))
            {
                throw new InvalidDataException("ASF media object fragments disagree or are not contiguous.");
            }
            if (part.Length != 0) { Parts.Add(part); }
            Length = checked(Length + part.Length);
        }

        public void ValidateComplete()
        {
            if (ExpectedLength is int expected && Length != expected)
            {
                throw new InvalidDataException("Truncated ASF media object.");
            }
        }
    }
}
