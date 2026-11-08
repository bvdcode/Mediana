// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Cli.Updates
{
    internal record UpdateRelease(Version Version, Uri DownloadUrl, long Size, byte[] Sha256);
}
