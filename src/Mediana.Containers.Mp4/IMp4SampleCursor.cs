// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Mp4
{
    internal interface IMp4SampleCursor
    {
        bool HasSample { get; }
        Mp4SampleLocation Current { get; }
        void Advance(Mp4SampleLocation current);
    }
}
