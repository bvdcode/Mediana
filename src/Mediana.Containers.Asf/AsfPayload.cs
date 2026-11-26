// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Asf
{
    internal readonly record struct AsfPayload(int Stream, int Object, int Offset, int? Size, long? Timestamp,
        bool KeyFrame, long Position, int Length);
}
