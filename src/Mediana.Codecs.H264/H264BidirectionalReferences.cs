// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal record H264BidirectionalReferences(IReadOnlyList<H264ReferencePicture?> L0, IReadOnlyList<H264ReferencePicture?> L1);
}
