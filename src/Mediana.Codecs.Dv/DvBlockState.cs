// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Dv
{
    internal struct DvBlockState
    {
        internal bool IsFieldTransform;
        internal bool Complete;
        internal int FactorOffset;
        internal int Coefficient;
        internal int PartialCode;
        internal int PartialLength;
    }
}
