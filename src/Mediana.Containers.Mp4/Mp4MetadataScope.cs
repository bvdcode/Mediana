// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Metadata;

namespace Mediana.Containers.Mp4
{
    internal record Mp4MetadataScope(Mp4MetadataValue[] Values, Mp4UserDataValue[] UserData, MediaTag[] Tags);
}
