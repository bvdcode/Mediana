// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents.OpenXml
{
    /// <summary>A package relationship. External targets are metadata and are never fetched.</summary>
    public record OpenXmlRelationship(string Id, string Type, string Target, bool External);
}
