// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal interface IH264CabacBinWriter
    {
        void WriteBin(bool value, ref H264CabacContext context);
        void WriteBypass(bool value);
        void WriteBypassBits(uint value, int count);
        void WriteTermination(bool value);
    }
}
