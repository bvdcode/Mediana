// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Metadata;

namespace Mediana.Tests
{
    internal class FixtureStreamMetadata(MediaKind kind) : StreamMetadata(kind);
}
