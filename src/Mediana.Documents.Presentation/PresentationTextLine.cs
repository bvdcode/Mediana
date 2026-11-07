// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents.Presentation
{
    internal record PresentationTextLine(IReadOnlyList<PresentationTextAtom> Atoms, double X, double Y, double Baseline, double Height, double ExtraSpace);
}
