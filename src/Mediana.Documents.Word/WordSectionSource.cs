// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Xml.Linq;

namespace Mediana.Documents.Word
{
    internal record WordSectionSource(XElement Properties, IReadOnlyList<XElement> Elements);
}
